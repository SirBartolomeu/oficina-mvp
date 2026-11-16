#!/usr/bin/env python3
"""Select the newest ECR image tagged with a Git commit SHA."""

import json
import re
import sys


def main() -> int:
    try:
        images = json.load(sys.stdin)["imageDetails"]
    except (ValueError, KeyError, TypeError) as error:
        print(f"Resposta ECR invalida: {error}", file=sys.stderr)
        return 2

    candidates = [
        (image.get("imagePushedAt", ""), tag)
        for image in images
        for tag in image.get("imageTags") or []
        if re.fullmatch(r"[0-9a-f]{40}", tag)
    ]
    if not candidates:
        print("Nenhuma imagem ECR com tag SHA de 40 caracteres", file=sys.stderr)
        return 1
    print(max(candidates)[1])
    return 0


if __name__ == "__main__":
    sys.exit(main())
