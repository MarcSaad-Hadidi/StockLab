FROM python:3.12-slim
WORKDIR /app/ml
COPY requirements.txt ./requirements.txt
RUN python -m pip install --no-cache-dir -r requirements.txt \
    && useradd --uid 1000 --create-home stocklab
COPY src ./src
ENV PYTHONPATH=/app/ml/src PYTHONUNBUFFERED=1
USER 1000:1000
ENTRYPOINT ["timeout", "--signal=TERM", "--kill-after=60s", "7200s", "python", "-m", "stocklab_ml.retraining.cli"]
CMD ["--once", "--config", "/mnt/stocklab/retraining/config.json", "--trigger-source", "scheduler", "--approve-private-s3"]
