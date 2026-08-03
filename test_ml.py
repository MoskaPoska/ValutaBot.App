import sys
import logging
sys.path.append('ml_service')
from model import ForexPredictor
from data_fetcher import TwelveDataFetcher
logging.basicConfig(level=logging.INFO)

print('--- ML Module 7 Backtest ---')
p = ForexPredictor('EURUSD', '1h')
print('1. Initializing Predictor for EURUSD 1h...')

print('2. Fetching recent candles for training...')
fetcher = TwelveDataFetcher()
candles = fetcher.fetch_history('EURUSD', '1h', limit=500, is_forex=True)

if not candles:
    print('Failed to fetch candles. Exiting.')
    sys.exit(1)

print(f'Fetched {len(candles)} candles.')

print('3. Training LightGBM globally...')
try:
    acc, auc = p.retrain_global(candles)
    print(f'Training complete! Accuracy: {acc:.2f}, AUC: {auc:.2f}')
except Exception as e:
    print(f'Training skipped or failed: {e}')

print('4. Generating Prediction for the latest candle...')
try:
    direction, conf = p.predict(candles)
    print(f'Prediction: {direction} (Confidence: {conf*100:.2f}%)')
except Exception as e:
    print(f'Prediction failed: {e}')
