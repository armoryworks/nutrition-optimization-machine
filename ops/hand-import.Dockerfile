FROM python:3.12.8-slim-bookworm

RUN apt-get update \
 && apt-get install -y --no-install-recommends poppler-utils tesseract-ocr tesseract-ocr-eng \
 && rm -rf /var/lib/apt/lists/* \
 && useradd --system --uid 10001 --no-create-home --shell /usr/sbin/nologin nomhand

COPY hand-import.py cookbook-extract.py recipe-source-attach.py /app/ops/
COPY hand-import/sources.csv hand-import/books.csv /app/ops/hand-import/

ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
USER nomhand
WORKDIR /work
ENTRYPOINT ["python3", "-I", "/app/ops/hand-import.py"]
