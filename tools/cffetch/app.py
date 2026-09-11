#!/usr/bin/env python3
"""
cffetch — берёт страницу обычным HTTP-запросом, но с отпечатком TLS
настоящего Chrome.

Зачем отдельная служба. Cloudflare выдаёт cookie `cf_clearance` не абстрактному
клиенту, а конкретному рукопожатию TLS: с той же cookie, но чужим отпечатком
приходит 403 и `cf-mitigated: challenge`. В .NET рукопожатие не настраивается,
поэтому отпечаток изображает `curl_cffi` здесь, а JacBlack спрашивает по HTTP.

Разделение труда: задачу решает браузер (FlareSolverr) один раз и отдаёт
cookie, дальше страницы идут сюда. Замер 07.09.2026 на rutracker — 0.13 с
против 3.9 с через браузер.

Ответ всегда JSON: {"status": <код сайта>, "body": "<html>"}.
Код 0 означает, что запрос не состоялся вовсе, — тогда JacBlack уходит
на браузер.
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

from curl_cffi import requests

PORT = int(os.environ.get("CFFETCH_PORT", "8192"))
DEFAULT_IMPERSONATE = os.environ.get("CFFETCH_IMPERSONATE", "chrome131")
MAX_BODY = 8 * 1024 * 1024

# Кодировка. Русские трекеры сплошь на windows-1251, и заголовок её называет
# не всегда — nnmclub объявляет её только в <meta>. Ошибиться тут дорого:
# заголовки раздач превратятся в кракозябры и уедут такими в базу.
META_CHARSET = re.compile(rb'charset=["\']?\s*([\w-]+)', re.I)


def decode(response):
    enc = (response.encoding or "").lower()

    if not enc or enc in ("iso-8859-1", "ascii"):
        found = META_CHARSET.search(response.content[:4096])
        enc = found.group(1).decode("ascii", "ignore") if found else "utf-8"

    try:
        return response.content.decode(enc, errors="replace")
    except LookupError:
        return response.content.decode("utf-8", errors="replace")


def fetch(task):
    url = task.get("url")
    if not url:
        return {"status": 0, "error": "адрес не указан"}

    headers = {}

    cookies = task.get("cookies")
    if cookies:
        headers["Cookie"] = cookies

    agent = task.get("userAgent")
    if agent:
        headers["User-Agent"] = agent

    kwargs = {
        "headers": headers,
        "impersonate": task.get("impersonate") or DEFAULT_IMPERSONATE,
        "timeout": int(task.get("timeout") or 25),
        "allow_redirects": True,
    }

    # Выход, если напрямую туда нельзя. 11.09.2026 Cloudflare забанил адрес
    # машины на kinozal: с него приходит блок-страница «Attention Required»,
    # а через туннель до любой нашей ноды тот же запрос проходит. Раз браузер
    # пошёл через выход, быстрый путь должен идти тем же — иначе он продолжит
    # стучаться с забаненного адреса.
    proxy = task.get("proxy")
    if proxy:
        # socks5h, а не socks5: имя сайта должен резолвить сам выход. С
        # обычным socks5 клиент резолвит домен у себя и отдаёт туннелю адрес,
        # а `ssh -D` такого не принимает — curl отвечает «Failed to receive
        # SOCKS response». Проверено 11.09.2026: socks5 падает, socks5h даёт
        # 302 с той же машины и тем же прокси.
        if proxy.startswith("socks5://"):
            proxy = "socks5h://" + proxy[len("socks5://"):]

        kwargs["proxies"] = {"http": proxy, "https": proxy}

    post = task.get("postData")

    if post is None:
        response = requests.get(url, **kwargs)
    else:
        headers.setdefault("Content-Type", "application/x-www-form-urlencoded")
        response = requests.post(url, data=post, **kwargs)

    # Отдельно говорим, вмешалась ли Cloudflare. Без этого признака отказ
    # самого сайта (403 на закрытый раздел) неотличим от отзыва cookie, и
    # JacBlack выбрасывал рабочую clearance на каждой такой странице.
    mitigated = "cf-mitigated" in {k.lower() for k in response.headers.keys()}

    if len(response.content) > MAX_BODY:
        return {"status": response.status_code, "cfMitigated": mitigated,
                "error": "страница слишком велика", "body": ""}

    return {"status": response.status_code, "cfMitigated": mitigated, "body": decode(response)}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def reply(self, payload, code=200):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.rstrip("/") in ("/health", ""):
            self.reply({"ok": True, "impersonate": DEFAULT_IMPERSONATE})
        else:
            self.reply({"status": 0, "error": "только POST /fetch"}, 404)

    def do_POST(self):
        if self.path.rstrip("/") != "/fetch":
            self.reply({"status": 0, "error": "только POST /fetch"}, 404)
            return

        try:
            length = int(self.headers.get("Content-Length") or 0)
            task = json.loads(self.rfile.read(length) or b"{}")
        except Exception as error:
            self.reply({"status": 0, "error": "разбор запроса: %s" % error}, 400)
            return

        try:
            self.reply(fetch(task))
        except Exception as error:
            # Отказ сайта — обычное дело, и падать из-за него служба не должна:
            # JacBlack по коду 0 просто уйдёт на браузер.
            self.reply({"status": 0, "error": "%s: %s" % (type(error).__name__, error)})

    def log_message(self, fmt, *args):
        # Свой лог короче стандартного и без адреса клиента: клиент всегда один.
        sys.stderr.write("cffetch %s\n" % (fmt % args))


if __name__ == "__main__":
    server = ThreadingHTTPServer(("0.0.0.0", PORT), Handler)
    sys.stderr.write("cffetch слушает %d, отпечаток по умолчанию %s\n" % (PORT, DEFAULT_IMPERSONATE))
    server.serve_forever()
