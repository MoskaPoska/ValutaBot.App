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
                candles = self._fetch_binance(1500)

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
