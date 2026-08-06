import time
import requests
import threading
from datetime import datetime

# Config
ASSETS = ["EURUSD_OTC"]
TIMEFRAME = "m1"
API_URL = "http://localhost:5000/api/analyze"
POLL_INTERVAL = 60  # seconds

# Stats
stats_lock = threading.Lock()
total_trades = 0
wins = 0
losses = 0

def log(msg):
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] {msg}")

def check_trade_result(asset, direction, entry_price, expiry_candles):
    global total_trades, wins, losses
    
    # Wait for expiry
    wait_time = expiry_candles * 60
    log(f"[WAIT] {asset} {direction} at {entry_price:.5f}. Waiting {wait_time}s...")
    time.sleep(wait_time)
    
    # Fetch close price
    try:
        resp = requests.get(f"{API_URL}?asset={asset}&timeframe={TIMEFRAME}", headers={"X-Paper-Trade-Bypass": "true"}, timeout=10)
        data = resp.json()
        exit_price = data['chartData'][-1]
        
        is_win = False
        if direction == "BUY" and exit_price > entry_price:
            is_win = True
        elif direction == "PUT" and exit_price < entry_price:
            is_win = True
            
        with stats_lock:
            total_trades += 1
            if is_win:
                wins += 1
                result_str = "[WIN]"
            else:
                losses += 1
                result_str = "[LOSS]"
                
            winrate = (wins / total_trades) * 100 if total_trades > 0 else 0
            
        log(f"[RESULT] {asset} {direction} | Entry: {entry_price:.5f} -> Exit: {exit_price:.5f} | {result_str} | WinRate: {winrate:.1f}% ({wins}W/{losses}L)")
            
    except Exception as e:
        log(f"[ERROR] Failed to check result for {asset}: {e}")

def run_paper_trader():
    log("=========================================")
    log("   VALUTA BOT PAPER TRADER STARTED       ")
    log("=========================================")
    
    while True:
        for asset in ASSETS:
            try:
                start_time = time.time()
                resp = requests.get(f"{API_URL}?asset={asset}&timeframe={TIMEFRAME}", headers={"X-Paper-Trade-Bypass": "true"}, timeout=10)
                elapsed = int((time.time() - start_time) * 1000)
                
                if resp.status_code == 200:
                    data = resp.json()
                    
                    if 'error' in data:
                        log(f"[{asset}] API Error: {data['error']}")
                        continue
                        
                    direction = data.get('direction', 'NEUTRAL')
                    if direction != "NEUTRAL":
                        prob = data.get('probability', 0)
                        expiry = data.get('expiryCandles', 5)
                        entry_price = data.get('chartData', [0])[-1]
                        ml_conf = data.get('lgbmConfidence', 0)
                        latency = data.get('latencies', {}).get('total', elapsed)
                        
                        log(f"[SIGNAL] {asset} {direction} | Prob: {prob}% (ML: {ml_conf}%) | Entry: {entry_price:.5f} | Latency: {latency}ms")
                        
                        # Start background thread to resolve trade
                        t = threading.Thread(target=check_trade_result, args=(asset, direction, entry_price, expiry))
                        t.daemon = True
                        t.start()
                    else:
                        latency = data.get('latencies', {}).get('total', elapsed)
                        log(f"[{asset}] NEUTRAL (Latency: {latency}ms)")
                else:
                    log(f"[{asset}] HTTP Error {resp.status_code}")
                    
            except Exception as e:
                log(f"[{asset}] Exception: {e}")
                
        time.sleep(POLL_INTERVAL)

if __name__ == "__main__":
    run_paper_trader()
