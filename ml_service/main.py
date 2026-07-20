"""
FastAPI ML microservice — LightGBM Forex/Crypto direction predictor.

Endpoints:
  GET  /health       → service status + model list
  POST /predict      → predict next candle direction
  POST /train        → train/retrain a model
  GET  /models       → list all loaded models
"""

from __future__ import annotations

import asyncio
import logging
import os
import time
import threading
from typing import Dict, List, Optional

from fastapi import FastAPI, HTTPException, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from model import ForexPredictor, TF_MAP, is_forex_symbol

# ── Logging ────────────────────────────────────────────────────────────────
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s [%(name)s] %(message)s",
)
log = logging.getLogger("ml-service")

# ── App ────────────────────────────────────────────────────────────────────
app = FastAPI(
    title="ValutaBot ML Service",
    description="LightGBM direction predictor for Forex/Crypto scalping",
    version="1.0.0",
)
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ── Global model registry ──────────────────────────────────────────────────
# key: "SYMBOL_interval"  (e.g. "BTCUSDT_1m")
_predictors: Dict[str, ForexPredictor] = {}
_registry_lock = threading.Lock()

START_TIME = time.time()


def _get_predictor(symbol: str, interval: str) -> ForexPredictor:
    key = f"{symbol.upper()}_{interval.lower()}"
    with _registry_lock:
        if key not in _predictors:
            p = ForexPredictor(symbol, interval)
            p._try_load()          # load from disk if exists
            _predictors[key] = p
        return _predictors[key]


# ── Request / Response schemas ─────────────────────────────────────────────

class CandleItem(BaseModel):
    open: float
    high: float
    low: float
    close: float
    volume: float


class PredictRequest(BaseModel):
    symbol: str                         # e.g. "BTCUSDT" or "EURUSD"
    interval: str                       # e.g. "1m" or "m5"
    candles: List[CandleItem]           # OHLCV history, latest last
    is_forex: bool = False


class PredictResponse(BaseModel):
    direction: str                      # "BUY" | "PUT" | "NEUTRAL"
    confidence: float                   # 0.0 – 1.0
    model_version: str
    accuracy: Optional[float] = None
    auc: Optional[float] = None


class TrainRequest(BaseModel):
    symbol: str
    interval: str
    candles: Optional[List[CandleItem]] = None   # if None → fetch from Binance


class TrainResponse(BaseModel):
    symbol: str
    interval: str
    n_train: int = 0
    accuracy: float = 0.0
    auc: float = 0.0
    version: str = ""
    error: Optional[str] = None


# ── Helpers ────────────────────────────────────────────────────────────────

def _normalize_interval(interval: str) -> str:
    """Unify interval string: 'm1'→'1m', '5m'→'5m', etc."""
    iv = interval.lower().strip()
    # Already canonical (Binance-style): "1m", "5m", "15m", "1h" etc.
    if iv in TF_MAP.values():
        return iv
    # ValutaBot-style: "m1", "m5", "h1" etc.
    return TF_MAP.get(iv, "1m")


def _candles_to_dicts(items: List[CandleItem]) -> List[dict]:
    return [{"open": c.open, "high": c.high, "low": c.low,
             "close": c.close, "volume": c.volume} for c in items]


# ── Routes ─────────────────────────────────────────────────────────────────

@app.get("/health")
def health():
    uptime = round(time.time() - START_TIME)
    with _registry_lock:
        models = [p.get_status() for p in _predictors.values()]
    return {
        "status": "ok",
        "uptime_seconds": uptime,
        "models_loaded": len(models),
        "models": models,
    }


@app.get("/models")
def list_models():
    with _registry_lock:
        return [p.get_status() for p in _predictors.values()]


@app.post("/predict", response_model=PredictResponse)
def predict(req: PredictRequest):
    if len(req.candles) < 60:
        raise HTTPException(
            status_code=422,
            detail=f"Need at least 60 candles for reliable prediction, got {len(req.candles)}",
        )

    interval = _normalize_interval(req.interval)
    predictor = _get_predictor(req.symbol, interval)

    # Auto-train in background if model is stale or missing
    if predictor.needs_retrain():
        t = threading.Thread(
            target=_background_train,
            args=(req.symbol, interval, None),
            daemon=True,
        )
        t.start()

    candle_dicts = _candles_to_dicts(req.candles)
    direction, confidence, version = predictor.predict(candle_dicts)

    meta = predictor._meta
    return PredictResponse(
        direction=direction,
        confidence=round(confidence, 4),
        model_version=version,
        accuracy=round(meta.accuracy, 4) if meta else None,
        auc=round(meta.auc, 4) if meta else None,
    )


@app.post("/train", response_model=TrainResponse)
def train(req: TrainRequest, background_tasks: BackgroundTasks):
    interval = _normalize_interval(req.interval)
    candle_dicts = _candles_to_dicts(req.candles) if req.candles else None

    # Run training in background so the API returns immediately
    background_tasks.add_task(_background_train, req.symbol, interval, candle_dicts)

    return TrainResponse(
        symbol=req.symbol,
        interval=interval,
        version="training-started",
    )


@app.post("/train/sync", response_model=TrainResponse)
def train_sync(req: TrainRequest):
    """Blocking train (useful for testing / initial setup)."""
    interval = _normalize_interval(req.interval)
    candle_dicts = _candles_to_dicts(req.candles) if req.candles else None
    predictor = _get_predictor(req.symbol, interval)
    report = predictor.train(candle_dicts)

    if "error" in report:
        return TrainResponse(symbol=req.symbol, interval=interval, error=report["error"])

    return TrainResponse(
        symbol=report.get("symbol", req.symbol),
        interval=report.get("interval", interval),
        n_train=report.get("n_train", 0),
        accuracy=report.get("accuracy", 0.0),
        auc=report.get("auc", 0.0),
        version=report.get("version", ""),
    )


def _background_train(symbol: str, interval: str, candles: Optional[list]):
    predictor = _get_predictor(symbol, interval)
    log.info(f"[BG Train] Starting {symbol}_{interval}")
    report = predictor.train(candles)
    log.info(f"[BG Train] Done: {report}")


# ── Startup auto-train ─────────────────────────────────────────────────────
# Pre-warm the most common symbols on startup (non-blocking)

_DEFAULT_SYMBOLS = os.getenv("PRETRAIN_SYMBOLS", "BTCUSDT,ETHUSDT,SOLUSDT").split(",")
_DEFAULT_INTERVALS = os.getenv("PRETRAIN_INTERVALS", "1m,2m,3m,5m,15m,30m,1h,4h").split(",")


@app.on_event("startup")
async def startup_event():
    log.info("[Startup] Launching background pre-training for all timeframes...")

    async def _train_all():
        await asyncio.sleep(5)   # give FastAPI time to finish startup
        for sym in _DEFAULT_SYMBOLS:
            sym = sym.strip().upper()
            if not sym:
                continue
            is_forex = is_forex_symbol(sym)
            
            for tf in _DEFAULT_INTERVALS:
                tf = tf.strip().lower()
                if not tf:
                    continue
                
                predictor = _get_predictor(sym, tf)
                if predictor.needs_retrain():
                    # Stagger to avoid TwelveData 8 requests/min rate limit (12s space = 5 reqs/min)
                    delay = 12.0 if is_forex else 1.5
                    
                    log.info(f"[Startup] Training {sym} ({tf}) | is_forex={is_forex}. Stagger delay={delay}s")
                    
                    t = threading.Thread(
                        target=_background_train,
                        args=(sym, tf, None),
                        daemon=True,
                    )
                    t.start()
                    
                    await asyncio.sleep(delay)

    asyncio.create_task(_train_all())



# ── Entry point ────────────────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    port = int(os.getenv("PORT", 8765))
    uvicorn.run("main:app", host="0.0.0.0", port=port, reload=False)
