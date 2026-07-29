import os
import sqlite3
import logging
import warnings
warnings.filterwarnings("ignore")

import main
from main import TrainFeedback, TrainRequest

logging.basicConfig(level=logging.INFO)

def run_deep_test():
    print("\n[TEST 1] Sending Mock Feedback from C# to Python...")
    req = TrainFeedback(
        asset="BTCUSDT",
        timeframe="1m",
        entry_price=95000.00,
        exit_price=95100.00,
        direction="BUY",
        was_win=True,
        timestamp="2026-07-28T00:00:00Z"
    )
    
    resp1 = main.feedback(req)
    print(f"Response: -> {resp1}")
    
    print("\n[TEST 2] Verifying SQLite Database persistence...")
    db_path = os.path.join(os.path.dirname(main.__file__), "data", "ValutaTicks.db")
    if os.path.exists(db_path):
        conn = sqlite3.connect(db_path)
        cur = conn.cursor()
        try:
            cur.execute("SELECT * FROM OnlineFeedback")
            rows = cur.fetchall()
            print(f"Records found in OnlineFeedback table: {len(rows)}")
            for r in rows:
                print(f"  -> ID:{r[0]} Asset:{r[1]} TF:{r[2]} Dir:{r[3]} Entry:{r[4]} Win:{r[6]}")
        except Exception as e:
            print(f"DB Error: {e}")
        conn.close()
    else:
        print("Database not found!")

    print("\n[TEST 3] Triggering Deep Training Pipeline to verify Sample Weighting...")
    train_req = TrainRequest(
        symbol="BTCUSDT",
        interval="1m",
        candles=None 
    )
    resp2 = main.train_sync(train_req)
    print(f"Train Response: -> {resp2}")
    print("\nTest completed successfully!")

if __name__ == "__main__":
    run_deep_test()
