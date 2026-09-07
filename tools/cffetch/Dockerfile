# Служба-помощник: ходит с отпечатком TLS настоящего Chrome.
# Нужна потому, что в .NET рукопожатие TLS не настраивается, а cookie
# `cf_clearance` Cloudflare выдаёт именно рукопожатию, а не клиенту.
FROM python:3.12-slim

RUN pip install --no-cache-dir "curl_cffi>=0.7,<1.0"

COPY app.py /app/app.py

ENV CFFETCH_PORT=8192 \
    CFFETCH_IMPERSONATE=chrome131

EXPOSE 8192

HEALTHCHECK --interval=60s --timeout=5s --start-period=10s --retries=3 \
    CMD python -c "import urllib.request,sys; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8192/health', timeout=4).status==200 else 1)"

CMD ["python", "-u", "/app/app.py"]
