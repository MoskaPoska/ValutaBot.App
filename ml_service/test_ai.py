import time
import requests
import logging
import warnings
warnings.filterwarnings("ignore")

import main
from main import TrainRequest, PredictRequest

logging.basicConfig(level=logging.INFO)

def run_ai_test():
    symbol = "BTCUSDT"
    interval = "1m"
    
    print(f"\n=======================================================")
    print(f"[STAGE 1] TRAINING LOCAL AI ON HISTORICAL DATA")
    print(f"=======================================================")
    print(f"Requesting deep training for {symbol} on {interval}...")
    
    # 1. Train
    train_req = TrainRequest(symbol=symbol, interval=interval, candles=None)
    train_resp = main.train_sync(train_req)
    
    print(f"\n-> Training Completed!")
    print(f"-> Model Version: {train_resp.version}")
    print(f"-> Dataset Size: {train_resp.n_train} candles")
    print(f"-> Validation Accuracy: {train_resp.accuracy * 100:.2f}%")
    print(f"-> ROC AUC Score: {train_resp.auc:.4f}")
    
    print(f"\n=======================================================")
    print(f"[STAGE 2] FETCHING LIVE MARKET DATA FOR INFERENCE")
    print(f"=======================================================")
    # 2. Fetch 60 live candles to simulate real-time bot behavior
    print("Fetching last 60 minutes of live market data from Binance...")
    
    url = f"https://api.binance.com/api/v3/klines?symbol={symbol}&interval={interval}&limit=60"
    r = requests.get(url)
    raw_klines = []
    for row in r.json():
        raw_klines.append({
            "open": float(row[1]),
            "high": float(row[2]),
            "low": float(row[3]),
            "close": float(row[4]),
            "volume": float(row[5])
        })
    
    # 3. Predict
    pred_req = PredictRequest(symbol=symbol, interval=interval, candles=raw_klines)
    pred_resp = main.predict(pred_req)
    
    print(f"-> AI Decision: {pred_resp.direction}")
    print(f"-> AI Confidence: {pred_resp.confidence * 100:.2f}%")
    
    if pred_resp.direction == "BUY":
        print("-> The AI expects the price to RISE in the next 3 candles.")
    elif pred_resp.direction == "PUT":
        print("-> The AI expects the price to FALL in the next 3 candles.")
    else:
        print("-> The AI is NEUTRAL (uncertain market conditions).")

if __name__ == "__main__":
    run_ai_test()
