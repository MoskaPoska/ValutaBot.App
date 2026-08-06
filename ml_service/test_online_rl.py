import os
import sys
import logging
import datetime
import time

# Set up basic logging to console
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')

sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from main import feedback, TrainFeedback

def main():
    print("=== Initiating Online RL (SGD) Test ===")
    
    # Create mock feedback for a winning CALL trade on EURUSD 1m
    req = TrainFeedback(
        asset="EURUSD",
        timeframe="1m",
        entry_price=1.10000,
        exit_price=1.10050,
        direction="CALL",
        was_win=True,
        timestamp=datetime.datetime.utcnow().isoformat()
    )
    
    start_time = time.perf_counter()
    
    print(f"Calling feedback endpoint for {req.asset} {req.timeframe}...")
    result = feedback(req)
    
    end_time = time.perf_counter()
    elapsed = (end_time - start_time) * 1000
    
    print(f"\n=== Feedback Processed in {elapsed:.2f} ms ===")
    print("Result:")
    print(result)

if __name__ == "__main__":
    main()
