"""
ForexPredictor: LightGBM binary classifier (BUY=1 / PUT=0).
Trains on historical Binance candles, predicts next-bar direction.
"""

from __future__ import annotations

import os
import time
import logging
import threading
import numpy as np
import pandas as pd
import requests
import joblib

from pathlib import Path
from typing import Optional, Tuple, List, Dict

try:
    import lightgbm as lgb
    from sklearn.model_selection import TimeSeriesSplit
    from sklearn.metrics import accuracy_score, roc_auc_score
    HAS_LGBM = True
except ImportError:
    HAS_LGBM = False

from features import build_features

log = logging.getLogger("predictor")

MODEL_DIR = Path(os.getenv("MODEL_DIR", "/app/models"))
RETRAIN_INTERVAL_H = int(os.getenv("RETRAIN_INTERVAL_H", "24"))
MIN_CONFIDENCE = float(os.getenv("MIN_CONFIDENCE", "0.60"))  # below → NEUTRAL
BINANCE_BASE = "https://api.binance.com"

# ── TwelveData Config ──
TWELVE_DATA_BASE = "https://api.twelvedata.com"
TWELVE_DATA_API_KEY = os.getenv("TwelveDataApiKey") or os.getenv("TWELVE_DATA_API_KEY")

TD_INTERVAL_MAP = {
    "1m": "1min", "2m": "2min", "3m": "5min", "5m": "5min",
    "10m": "10min", "15m": "15min", "30m": "30min", "45m": "45min",
    "1h": "1h", "2h": "2h", "4h": "4h", "1d": "1day"
}

def is_forex_symbol(symbol: str) -> bool:
    sym = symbol.upper()
    if sym in ["GOLD", "SILVER", "BRENT", "OIL", "XAUUSD", "XAGUSD"]:
        return True
    # Most Forex assets are 6 letters (EURUSD, USDJPY) and do not end with USDT
    if len(sym) == 6 and not sym.endswith("USDT"):
        return True
    return False

def to_twelvedata_symbol(symbol: str) -> str:
    sym = symbol.upper()
    if sym in ["GOLD", "XAUUSD"]:
        return "XAU/USD"
    if sym in ["SILVER", "XAGUSD"]:
        return "XAG/USD"
    # EURUSD -> EUR/USD
    if len(sym) == 6:
        return f"{sym[:3]}/{sym[3:]}"
    return sym

def _interpolate_subminute(m1_candles: List[Dict], interval: str) -> List[Dict]:
    """Interpolate 1-minute candles into sub-minute steps (s5, s10, s15, s30)."""
    sec = int(interval[1:]) if (interval.startswith("s") and len(interval) > 1) else 60
    if sec >= 60:
        return m1_candles
        
    sub_per_min = 60 // sec
    interpolated = []
    
    import random
    
    for m in m1_candles:
        start_price = m["open"]
        end_price = m["close"]
        price_range = end_price - start_price
        high_limit = m["high"]
        low_limit = m["low"]
        vol_step = (high_limit - low_limit) / sub_per_min
        
        for i in range(sub_per_min):
            frac_start = i / sub_per_min
            frac_end = (i + 1) / sub_per_min
            
            o = start_price + price_range * frac_start
            c = start_price + price_range * frac_end
            
            rand_offset = (random.random() - 0.5) * vol_step * 0.5
            h = max(o, c) + abs(rand_offset)
            l = min(o, c) - abs(rand_offset)
            
            h = min(h, high_limit)
            l = max(l, low_limit)
            
            interpolated.append({
                "open": o,
                "high": h,
                "low": l,
                "close": c,
                "volume": m["volume"] / sub_per_min
            })
            
    return interpolated



# ── Timeframe → Binance interval string ──
TF_MAP = {
    "s3": "1m", "s5": "1m", "s10": "1m", "s15": "1m", "s30": "1m",
    "m1": "1m", "m2": "2m", "m3": "3m", "m5": "5m", "m10": "10m",
    "m15": "15m", "m30": "30m", "h1": "1h", "h4": "4h",
    "1m": "1m", "5m": "5m", "15m": "15m", "30m": "30m", "1h": "1h",
}

LGBM_PARAMS = {
    "objective": "binary",
    "metric": "auc",
    "n_estimators": 500,
    "learning_rate": 0.02,
    "max_depth": 6,
    "num_leaves": 31,
    "min_child_samples": 30,
    "feature_fraction": 0.8,
    "bagging_fraction": 0.8,
    "bagging_freq": 5,
    "lambda_l1": 0.1,
    "lambda_l2": 0.1,
    "verbose": -1,
}


class ModelMeta:
    """Metadata stored alongside each model file."""
    def __init__(self, accuracy: float, auc: float, n_train: int,
                 trained_at: float, version: str):
        self.accuracy = accuracy
        self.auc = auc
        self.n_train = n_train
        self.trained_at = trained_at
        self.version = version


class ForexPredictor:
    """
    One predictor per (symbol, interval).  All instances are held in the
    global registry `_predictors` in main.py.
    """

    def __init__(self, symbol: str, interval: str):
        self.symbol = symbol.upper()
        self.interval = interval.lower()
        self._key = f"{self.symbol}_{self.interval}"
        self._model: Optional[lgb.LGBMClassifier] = None
        self._meta: Optional[ModelMeta] = None
        self._lock = threading.Lock()
        MODEL_DIR.mkdir(parents=True, exist_ok=True)

    # ── Public API ──────────────────────────────────────────────────────────

    def predict(self, candles: List[Dict]) -> Tuple[str, float, str]:
        """
        Predict next candle direction from supplied candle list.
        Returns (direction, confidence, model_version).
        direction: "BUY" | "PUT" | "NEUTRAL"
        confidence: 0.0 – 1.0
        """
        if not HAS_LGBM:
            return "NEUTRAL", 0.5, "no-lgbm"

        with self._lock:
            model = self._model
            meta = self._meta

        if model is None:
            # Try loading from disk
            self._try_load()
            with self._lock:
                model = self._model
                meta = self._meta

        if model is None:
            return "NEUTRAL", 0.5, "not-trained"

        try:
            feats = build_features(candles)
            if feats.empty or len(feats) < 5:
                return "NEUTRAL", 0.5, meta.version if meta else "no-feats"

            # Use last row as the current candle state
            X_last = feats.iloc[[-1]]
            prob = float(model.predict_proba(X_last)[0, 1])

            version = meta.version if meta else self._key

            if prob >= MIN_CONFIDENCE:
                return "BUY", prob, version
            elif prob <= (1.0 - MIN_CONFIDENCE):
                return "PUT", 1.0 - prob, version
            else:
                confidence = abs(prob - 0.5) * 2   # 0 at boundary, 1 at extremes
                return "NEUTRAL", 0.5 + confidence * 0.15, version

        except Exception as e:
            log.error(f"[Predict] {self._key}: {e}")
            return "NEUTRAL", 0.5, "error"

    def train(self, candles: Optional[List[Dict]] = None) -> Dict:
        """
        Train model. If candles not provided, fetch from Binance.
        Returns training report dict.
        """
        if not HAS_LGBM:
            return {"error": "lightgbm not installed"}

        log.info(f"[Train] Starting training for {self._key}")
        try:
            if candles is None:
                # 1500 sub-minute candles require at least 750 1-minute candles
                limit = 750 if self.interval.startswith("s") else 1500
                if is_forex_symbol(self.symbol):
                    candles = self._fetch_twelvedata(limit)
                else:
                    candles = self._fetch_binance(limit)

            # If sub-minute timeframe, interpolate 1-minute history
            if self.interval.startswith("s") and len(candles) > 0:
                candles = _interpolate_subminute(candles, self.interval)
                if len(candles) > 1500:
                    candles = candles[-1500:]

            if len(candles) < 150:
                return {"error": f"Not enough candles: {len(candles)} < 150"}

            feats = build_features(candles)
            if feats.empty or len(feats) < 100:
                return {"error": "Feature engineering yielded too few rows"}

            # Build target: next candle close > current close  → 1
            closes = np.array([c["close"] for c in candles])
            target_raw = (closes[1:] > closes[:-1]).astype(int)
            # Align features (features is shorter by ~50 due to rolling NaN drop)
            feat_indices = feats.index.values
            # target index: for feature at position i in original, label = target_raw[i]
            target_aligned = target_raw[feat_indices]

            X = feats.values.astype(np.float32)
            y = target_aligned

            # TimeSeriesSplit CV
            tscv = TimeSeriesSplit(n_splits=5)
            val_accs, val_aucs = [], []

            for train_idx, val_idx in tscv.split(X):
                X_tr, X_val = X[train_idx], X[val_idx]
                y_tr, y_val = y[train_idx], y[val_idx]

                m = lgb.LGBMClassifier(**LGBM_PARAMS)
                m.fit(
                    X_tr, y_tr,
                    eval_set=[(X_val, y_val)],
                    callbacks=[lgb.early_stopping(50, verbose=False),
                               lgb.log_evaluation(period=-1)]
                )
                preds = m.predict(X_val)
                probs = m.predict_proba(X_val)[:, 1]
                val_accs.append(accuracy_score(y_val, preds))
                try:
                    val_aucs.append(roc_auc_score(y_val, probs))
                except Exception:
                    val_aucs.append(0.5)

            avg_acc = float(np.mean(val_accs))
            avg_auc = float(np.mean(val_aucs))

            # Final model on all data
            final_model = lgb.LGBMClassifier(**LGBM_PARAMS)
            final_model.fit(X, y, callbacks=[lgb.log_evaluation(period=-1)])

            version = f"lgbm-v1-{self._key}-{int(time.time())}"
            meta = ModelMeta(
                accuracy=avg_acc,
                auc=avg_auc,
                n_train=len(X),
                trained_at=time.time(),
                version=version,
            )

            self._save(final_model, meta)

            with self._lock:
                self._model = final_model
                self._meta = meta

            report = {
                "symbol": self.symbol,
                "interval": self.interval,
                "n_train": len(X),
                "accuracy": round(avg_acc, 4),
                "auc": round(avg_auc, 4),
                "version": version,
            }
            log.info(f"[Train] Done: {report}")
            return report

        except Exception as e:
            log.error(f"[Train] {self._key}: {e}", exc_info=True)
            return {"error": str(e)}

    def needs_retrain(self) -> bool:
        with self._lock:
            meta = self._meta
        if meta is None:
            return True
        age_h = (time.time() - meta.trained_at) / 3600
        return age_h >= RETRAIN_INTERVAL_H

    def get_status(self) -> Dict:
        with self._lock:
            meta = self._meta
        if meta is None:
            return {"status": "not-trained", "key": self._key}
        return {
            "key": self._key,
            "accuracy": meta.accuracy,
            "auc": meta.auc,
            "n_train": meta.n_train,
            "version": meta.version,
            "age_hours": round((time.time() - meta.trained_at) / 3600, 1),
        }

    # ── Internal helpers ────────────────────────────────────────────────────

    def _model_path(self) -> Path:
        return MODEL_DIR / f"{self._key}.pkl"

    def _save(self, model, meta: ModelMeta):
        joblib.dump({"model": model, "meta": meta}, self._model_path())

    def _try_load(self):
        p = self._model_path()
        if p.exists():
            try:
                data = joblib.load(p)
                with self._lock:
                    self._model = data["model"]
                    self._meta = data["meta"]
                log.info(f"[Load] Loaded model from {p}")
            except Exception as e:
                log.warning(f"[Load] Failed to load {p}: {e}")

    def _fetch_binance(self, limit: int = 1500) -> List[Dict]:
        """Fetch historical klines from Binance REST API."""
        binance_interval = TF_MAP.get(self.interval, self.interval)
        url = f"{BINANCE_BASE}/api/v3/klines"
        params = {"symbol": self.symbol, "interval": binance_interval, "limit": min(limit, 1500)}
        resp = requests.get(url, params=params, timeout=15)
        resp.raise_for_status()
        raw = resp.json()
        candles = [
            {
                "open":   float(k[1]),
                "high":   float(k[2]),
                "low":    float(k[3]),
                "close":  float(k[4]),
                "volume": float(k[5]),
            }
            for k in raw
        ]
        return candles

    def _fetch_twelvedata(self, limit: int = 1500) -> List[Dict]:
        """Fetch historical candles from TwelveData REST API."""
        if not TWELVE_DATA_API_KEY:
            raise ValueError("TwelveDataApiKey environment variable is not configured on the service.")

        td_symbol = to_twelvedata_symbol(self.symbol)
        td_interval = TD_INTERVAL_MAP.get(self.interval, "1min")
        
        log.info(f"[TwelveData] Fetching history for {td_symbol} ({td_interval}), limit={limit}")
        
        url = f"{TWELVE_DATA_BASE}/time_series"
        params = {
            "symbol": td_symbol,
            "interval": td_interval,
            "outputsize": min(limit, 5000),
            "apikey": TWELVE_DATA_API_KEY
        }
        
        resp = requests.get(url, params=params, timeout=20)
        resp.raise_for_status()
        data = resp.json()
        
        if data.get("status") == "error":
            raise Exception(f"TwelveData API error: {data.get('message')}")
            
        raw_candles = data.get("values")
        if not raw_candles:
            raise Exception(f"TwelveData returned no candles for {td_symbol}")
            
        # Reversing so that the oldest is at index 0 and latest is at index -1
        raw_candles.reverse()
        
        candles = [
            {
                "open":   float(k["open"]),
                "high":   float(k["high"]),
                "low":    float(k["low"]),
                "close":  float(k["close"]),
                "volume": float(k.get("volume", 0.0) or 0.0),
            }
            for k in raw_candles
        ]
        log.info(f"[TwelveData] Successfully fetched {len(candles)} candles for {td_symbol}")
        return candles

