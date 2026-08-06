import os
import sys
import json
import logging

# Set up basic logging to console
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')

# Ensure we can import from local directory
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

os.environ['TwelveDataApiKey'] = '3e0d610500f0414282d471471f59504e'
os.environ['TWELVE_DATA_API_KEY'] = '3e0d610500f0414282d471471f59504e'

from model import ForexPredictor

def main():
    print("=== Initiating AI Global Retraining Test ===")
    
    symbol = "EURUSD"
    interval = "1m"
    
    # Initialize the model instance
    print(f"Loading ModelCache for {symbol}_{interval}...")
    model_instance = ForexPredictor(symbol, interval)
    
    # Force global retrain by passing None for candles
    print("Triggering model.train(candles=None) -> This should fetch from Binance...")
    report = model_instance.train(candles=None)
    
    print("\n=== Training Complete ===")
    print("Report Summary:")
    print(json.dumps(report, indent=2, ensure_ascii=False))

if __name__ == "__main__":
    main()
