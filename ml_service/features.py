"""
Feature engineering for Forex/Crypto LightGBM predictor.
Takes OHLCV candle arrays and returns a feature DataFrame.
"""

import numpy as np
import pandas as pd
from typing import List, Dict


def _rsi(close: np.ndarray, period: int = 14) -> np.ndarray:
    delta = np.diff(close, prepend=close[0])
    gain = np.where(delta > 0, delta, 0.0)
    loss = np.where(delta < 0, -delta, 0.0)
    avg_gain = pd.Series(gain).ewm(alpha=1 / period, adjust=False).mean().values
    avg_loss = pd.Series(loss).ewm(alpha=1 / period, adjust=False).mean().values
    rs = np.where(avg_loss == 0, 100, avg_gain / (avg_loss + 1e-10))
    return 100 - 100 / (1 + rs)


def _ema(close: np.ndarray, period: int) -> np.ndarray:
    return pd.Series(close).ewm(span=period, adjust=False).mean().values


def _atr(high: np.ndarray, low: np.ndarray, close: np.ndarray, period: int = 14) -> np.ndarray:
    prev_close = np.roll(close, 1)
    prev_close[0] = close[0]
    tr = np.maximum.reduce([high - low, np.abs(high - prev_close), np.abs(low - prev_close)])
    return pd.Series(tr).ewm(alpha=1 / period, adjust=False).mean().values


def _bollinger_z(close: np.ndarray, period: int = 20) -> np.ndarray:
    s = pd.Series(close)
    ma = s.rolling(period).mean()
    std = s.rolling(period).std()
    return ((s - ma) / (std + 1e-10)).values


def _macd(close: np.ndarray, fast=12, slow=26, signal=9):
    fast_ema = _ema(close, fast)
    slow_ema = _ema(close, slow)
    macd_line = fast_ema - slow_ema
    signal_line = _ema(macd_line, signal)
    return macd_line, signal_line


def _rolling_std(close: np.ndarray, period: int = 10) -> np.ndarray:
    return pd.Series(close).rolling(period).std().values


def _volume_ma(volume: np.ndarray, period: int = 20) -> np.ndarray:
    return pd.Series(volume).rolling(period).mean().values


def _linreg_slope(close: np.ndarray, period: int = 20) -> np.ndarray:
    """Rolling linear regression slope (normalized by price)."""
    slopes = np.zeros(len(close))
    x = np.arange(period, dtype=float)
    x -= x.mean()
    for i in range(period - 1, len(close)):
        y = close[i - period + 1: i + 1].astype(float)
        y_mean = y.mean()
        slope = (x * (y - y_mean)).sum() / ((x * x).sum() + 1e-10)
        slopes[i] = slope / (y_mean + 1e-10)   # normalize by price level
    return slopes


def _hurst_approx(close: np.ndarray, lag_max: int = 16) -> np.ndarray:
    """Approximate rolling Hurst exponent (simplified, window=lag_max*2)."""
    result = np.full(len(close), 0.5)
    win = lag_max * 2
    for i in range(win, len(close)):
        seg = close[i - win: i]
        try:
            changes2 = np.diff(seg, n=2)
            changes16 = seg[16:] - seg[:-16]
            std2 = np.std(changes2) + 1e-12
            std16 = np.std(changes16) + 1e-12
            h = np.log(std16 / std2) / np.log(8)
            result[i] = np.clip(h, 0.0, 1.0)
        except Exception:
            result[i] = 0.5
    return result


def build_features(candles: List[Dict]) -> pd.DataFrame:
    """
    Build feature matrix from list of OHLCV candle dicts.
    Each dict: {'open', 'high', 'low', 'close', 'volume'}
    Returns DataFrame with one row per candle, features only (no NaN rows).
    """
    df = pd.DataFrame(candles)
    df.columns = [c.lower() for c in df.columns]

    o = df['open'].values.astype(float)
    h = df['high'].values.astype(float)
    lo = df['low'].values.astype(float)
    c = df['close'].values.astype(float)
    v = df['volume'].values.astype(float)

    feats = {}

    # ── Trend / Momentum ──
    feats['ema9']       = _ema(c, 9)
    feats['ema21']      = _ema(c, 21)
    feats['ema50']      = _ema(c, 50)
    feats['ema_ratio_9_21']  = feats['ema9'] / (feats['ema21'] + 1e-10) - 1
    feats['close_vs_ema9']   = c / (feats['ema9'] + 1e-10) - 1
    feats['close_vs_ema21']  = c / (feats['ema21'] + 1e-10) - 1
    feats['close_vs_ema50']  = c / (feats['ema50'] + 1e-10) - 1

    macd_line, macd_sig = _macd(c)
    feats['macd']       = macd_line / (np.abs(c) + 1e-10)
    feats['macd_hist']  = (macd_line - macd_sig) / (np.abs(c) + 1e-10)

    feats['linreg_slope'] = _linreg_slope(c, 20)
    feats['hurst']        = _hurst_approx(c, 16)

    # ── Oscillators ──
    feats['rsi14'] = _rsi(c, 14) / 100.0 - 0.5   # centered on 0
    feats['rsi7']  = _rsi(c, 7)  / 100.0 - 0.5
    feats['bb_z']  = _bollinger_z(c, 20)

    # ── Volatility ──
    atr = _atr(h, lo, c, 14)
    feats['atr_norm']     = atr / (c + 1e-10)
    feats['rolling_std']  = _rolling_std(c, 10) / (c + 1e-10)

    # ── Price Returns ──
    for lag in [1, 2, 3, 5, 10]:
        ret = np.zeros(len(c))
        ret[lag:] = (c[lag:] - c[:-lag]) / (c[:-lag] + 1e-10)
        feats[f'ret{lag}'] = ret

    # ── Candle Structure ──
    candle_range = (h - lo) + 1e-10
    feats['body_ratio']    = np.abs(c - o) / candle_range
    feats['upper_wick']    = (h - np.maximum(o, c)) / candle_range
    feats['lower_wick']    = (np.minimum(o, c) - lo) / candle_range
    feats['candle_dir']    = np.sign(c - o)

    # ── Volume ──
    vol_ma = _volume_ma(v, 20)
    feats['vol_ratio']     = v / (vol_ma + 1e-10)
    feats['vol_ma']        = vol_ma / (vol_ma.mean() + 1e-10)

    # ── High/Low channel position ──
    high20 = pd.Series(h).rolling(20).max().values
    low20  = pd.Series(lo).rolling(20).min().values
    range20 = high20 - low20 + 1e-10
    feats['channel_pos'] = (c - low20) / range20

    # ── Time / Session (sinusoidal encoding so hour=23 is close to hour=0) ──
    # Placeholder: real timestamp not available here, use 0 (client can enrich)
    feats['hour_sin'] = np.zeros(len(c))
    feats['hour_cos'] = np.zeros(len(c))

    result = pd.DataFrame(feats, index=df.index)

    # Drop rows with any NaN (first ~50 candles will have NaN from rolling)
    result = result.dropna()

    return result
