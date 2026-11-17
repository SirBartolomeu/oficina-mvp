#!/usr/bin/env python3
"""Render the initial K3s manifests from Terraform outputs without editing Git files."""

import json
import re
import sys


def main() -> None:
    if len(sys.argv) != 5:
        raise SystemExit("usage: render-distributed.py CONFIG OS_IMAGE EXECUTION_IMAGE BILLING_IMAGE")
    with open(sys.argv[1], encoding="utf-8-sig") as source:
        config = json.load(source)
    rendered = sys.stdin.read()

    def replace(old: str, new: str, count: int) -> None:
        nonlocal rendered
        found = rendered.count(old)
        if found != count:
            raise SystemExit(f"Expected {count} occurrences of {old!r}; found {found}")
        rendered = rendered.replace(old, new)

    images = sys.argv[2:]
    for image in images:
        if not re.fullmatch(r"[a-z0-9.-]+\.dkr\.ecr\.[a-z0-9-]+\.amazonaws\.com/[a-z0-9._/-]+:[0-9a-f]{40}", image):
            raise SystemExit(f"Invalid ECR image URI: {image}")

    for old, new in zip(
        ("oficina-mvp-api:local", "oficina-execution:local", "oficina-billing:local"), images
    ):
        replace("image: " + old, "image: " + new, 1)

    replace("AWS__Region: us-east-1", "AWS__Region: " + json.dumps(config["awsRegion"]), 3)
    replace("DynamoDb__Table: oficina_billing", "DynamoDb__Table: " + json.dumps(config["billingTableName"]), 1)
    queues = config["queueUrls"]
    for marker, key, count in (
        ("REPLACE_WITH_BILLING_WORKORDERS_QUEUE_URL", "billing_workorders", 2),
        ("REPLACE_WITH_EXECUTION_COMMANDS_QUEUE_URL", "execution_commands", 2),
        ("REPLACE_WITH_OS_EVENTS_QUEUE_URL", "os_events", 3),
    ):
        replace(marker, json.dumps(queues[key]), count)
    if "REPLACE_WITH_" in rendered:
        raise SystemExit("Unresolved manifest placeholder")
    sys.stdout.write(rendered)


if __name__ == "__main__":
    main()
