import sys
import os
import time
sys.path.append(os.path.abspath(r"C:\Users\bural\source\repos\ValutaBot.App\ml_service"))

from model import ForexPredictor

def run_test():
    print("Testing ML Service Training with new Features & Horizon...")
    # Initialize predictor for BTCUSDT on 5-minute timeframe
    predictor = ForexPredictor("BTCUSDT", "m5")
    
    start = time.time()
    # Call train (will fetch binance candles automatically since candles=None)
    report = predictor.train()
    elapsed = time.time() - start
    
    print("\n=== TRAINING REPORT ===")
    if "error" in report:
        print(f"FAILED: {report['error']}")
    else:
        print(f"Symbol: {report['symbol']}")
        print(f"Interval: {report['interval']}")
        print(f"Training Rows: {report['n_train']}")
        print(f"Accuracy (CV): {report['accuracy']*100:.2f}%")
        print(f"AUC (CV): {report['auc']:.4f}")
        print(f"Model Version: {report['version']}")
        print(f"Time Taken: {elapsed:.2f}s")
        
        # Test a prediction
        status = predictor.get_status()
        print(f"\nModel Status: {status}")

if __name__ == "__main__":
    run_test()
