FROM python:3.11-slim

WORKDIR /app

# Install system deps (LightGBM needs libgomp)
RUN apt-get update && apt-get install -y --no-install-recommends \
        libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# Directory for persisted model files
RUN mkdir -p /app/models

EXPOSE 8765

ENV PORT=8765 \
    MODEL_DIR=/app/models \
    RETRAIN_INTERVAL_H=24 \
    MIN_CONFIDENCE=0.60 \
    PRETRAIN_SYMBOLS=BTCUSDT,ETHUSDT,SOLUSDT \
    PRETRAIN_INTERVAL=1m

CMD ["python", "main.py"]
