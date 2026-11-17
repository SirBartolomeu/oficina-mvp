#!/usr/bin/env python3
"""Exercise the distributed work-order flow against the configured APIs."""

import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
import uuid


def request(method, url, payload=None, headers=None):
    data = None if payload is None else json.dumps(payload).encode()
    request_headers = {"Content-Type": "application/json", **(headers or {})}
    req = urllib.request.Request(url, data=data, headers=request_headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as response:
            return json.load(response), dict(response.headers)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{method} {url}: HTTP {error.code}") from error


def main():
    os_url = os.environ["OFICINA_OS_URL"].rstrip("/")
    execution_url = os.environ["OFICINA_EXECUTION_URL"].rstrip("/")
    admin_password = os.environ["OFICINA_ADMIN_PASSWORD"]
    internal_token = os.environ["OFICINA_EXECUTION_TOKEN"]
    admin_user = os.environ.get("OFICINA_ADMIN_USER", "admin")

    token_response, _ = request("POST", os_url + "/api/auth/token", {
        "username": admin_user, "password": admin_password
    })
    token = token_response["accessToken"]
    admin_headers = {"Authorization": "Bearer " + token}
    customers, _ = request("GET", os_url + "/api/customers", headers=admin_headers)
    if not any(customer.get("document") == "52998224725" for customer in customers):
        request("POST", os_url + "/api/customers", {
            "name": "Cliente Demo AWS", "document": "52998224725",
            "phone": None, "email": None
        }, admin_headers)
    service, _ = request("POST", execution_url + "/internal/catalog/services", {
        "name": "Demo AWS " + uuid.uuid4().hex[:8],
        "description": "Validacao distribuida",
        "laborPrice": 125,
        "averageDurationMinutes": 30,
    }, {"X-Internal-Token": internal_token})
    order, response_headers = request("POST", os_url + "/api/work-orders", {
        "customerDocument": "52998224725",
        "vehicle": {"licensePlate": "BRA2E19", "brand": "Volkswagen", "model": "Gol", "year": 2021},
        "services": [{"serviceId": service["id"], "quantity": 1}],
        "parts": [],
        "branchId": "sp-centro",
    }, admin_headers)
    order_id = order["id"]
    trace_id = response_headers.get("X-Trace-Id", "")
    if not re.fullmatch(r"[0-9a-f]{32}", trace_id):
        raise RuntimeError("Work-order response did not include a valid trace ID")

    for _ in range(45):
        current, _ = request("GET", os_url + "/api/work-orders/" + order_id,
                             headers=admin_headers)
        if current.get("processState") == "AwaitingApproval":
            auth_url = os.environ.get("OFICINA_AUTH_URL")
            if auth_url:
                client, _ = request("POST", auth_url, {"cpf": "52998224725"})
                tracking, _ = request("GET", os_url + "/api/client/work-orders/" + order_id,
                                      headers={"Authorization": "Bearer " + client["accessToken"]})
                if tracking.get("workOrderId") != order_id:
                    raise RuntimeError("Client JWT did not return the created work order")
            print(f"OS={order_id} status=AwaitingApproval trace={trace_id}")
            return
        time.sleep(2)
    raise RuntimeError(f"OS {order_id} did not reach AwaitingApproval within 90 seconds")


if __name__ == "__main__":
    try:
        main()
    except (KeyError, RuntimeError, urllib.error.URLError) as error:
        print(f"Distributed flow failed: {error}", file=sys.stderr)
        sys.exit(1)
