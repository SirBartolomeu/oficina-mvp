#!/usr/bin/env python3
"""Union Cobertura reports for the OS service and enforce its line coverage gate."""

import argparse
import hashlib
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


PACKAGES = {
    "OficinaMvp.Domain",
    "OficinaMvp.Application",
    "OficinaMvp.Infrastructure",
    "OficinaMvp.Api",
}


def report_path(value: str) -> Path:
    path = Path(value)
    if path.is_file():
        return path
    reports = list(path.rglob("coverage.cobertura.xml")) if path.is_dir() else []
    if not reports:
        raise ValueError(f"Nenhum relatório Cobertura em {path}")
    hashes = {hashlib.sha256(report.read_bytes()).digest() for report in reports}
    if len(hashes) != 1:
        raise ValueError(f"Relatórios Cobertura diferentes em {path}; encontrados {len(reports)}")
    return reports[0]


def source_key(package: str, filename: str) -> str:
    normalized = filename.replace("\\", "/")
    prefix = package + "/"
    relative = normalized.rsplit(prefix, 1)[-1] if prefix in normalized else normalized
    return f"{package}/{relative}"


def collect(paths: list[Path]) -> dict[str, dict[int, bool]]:
    union: dict[str, dict[int, bool]] = defaultdict(dict)
    seen_packages = set()
    for path in paths:
        root = ET.parse(path).getroot()
        report_lines = 0
        for package in root.findall("./packages/package"):
            name = package.get("name")
            if name not in PACKAGES:
                continue
            seen_packages.add(name)
            for klass in package.findall("./classes/class"):
                key = source_key(name, klass.get("filename", ""))
                # EF Core creates these migration and snapshot files; no application logic lives here.
                if key.startswith("OficinaMvp.Infrastructure/Persistence/Migrations/"):
                    continue
                for line in klass.findall("./lines/line"):
                    number = int(line.get("number", "0"))
                    if number <= 0:
                        continue
                    hits = int(line.get("hits", "0")) > 0
                    union[key][number] = union[key].get(number, False) or hits
                    report_lines += 1
        if report_lines == 0:
            raise ValueError(f"Nenhuma linha instrumentada de OS em {path}")
    if seen_packages != PACKAGES:
        raise ValueError(f"Pacotes ausentes: {', '.join(sorted(PACKAGES - seen_packages))}")
    return union


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--domain", required=True, help="Diretório ou XML Cobertura da suíte Domain")
    parser.add_argument("--integration", required=True, help="Diretório ou XML Cobertura da suíte Integration")
    parser.add_argument("--postgres", required=True, help="Diretório ou XML Cobertura da suíte PostgreSQL")
    parser.add_argument("--minimum", type=float, default=80.0)
    args = parser.parse_args()
    try:
        paths = [report_path(value) for value in (args.domain, args.integration, args.postgres)]
        union = collect(paths)
    except (ValueError, ET.ParseError, OSError) as error:
        parser.error(str(error))

    total = sum(len(lines) for lines in union.values())
    covered = sum(sum(lines.values()) for lines in union.values())
    percent = 100 * covered / total
    print(f"OS: {covered}/{total} linhas = {percent:.2f}% (mínimo {args.minimum:.2f}%).")
    for package in sorted(PACKAGES):
        files = [lines for key, lines in union.items() if key.startswith(package + "/")]
        count = sum(len(lines) for lines in files)
        hit = sum(sum(lines.values()) for lines in files)
        print(f"  {package}: {hit}/{count} = {100 * hit / count:.2f}%")
    if percent < args.minimum:
        print("Cobertura OS abaixo do mínimo.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
