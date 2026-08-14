#!/usr/bin/env python3
"""Safely replace JPS rows in a complete benchmark CSV with a JPS-only rerun."""

from __future__ import annotations

import argparse
import csv
import os
from dataclasses import dataclass
from pathlib import Path


ALGORITHM_NAME = "JumpPointSearch"

# These fields define the experiment input and must match exactly.
INPUT_FIELDS = [
    "TestID",
    "StartX",
    "StartY",
    "TargetX",
    "TargetY",
    "Scenario",
    "ObstacleDensity",
    "MapTopology",
    "MapSeed",
    "MapDensity",
    "MapWidth",
    "MapHeight",
    "DistanceBucket",
    "EuclideanDistance",
    "OctagonalDistance",
    "ReferenceShortestPathLength",
]

# Optimising storage must not alter any logical pathfinding result.
LOGICAL_RESULT_FIELDS = [
    "PathFound",
    "ExploredNodes",
    "JumpScannedCells",
    "PathLength",
    "PathCost10_14",
    "DirectionChanges",
    "PathSmoothness",
    "PathRecalculations",
]


@dataclass(frozen=True)
class CsvRecord:
    raw_line: str
    values: dict[str, str]


@dataclass(frozen=True)
class CsvFile:
    header_line: str
    fieldnames: list[str]
    records: list[CsvRecord]


def read_csv_file(path: Path) -> CsvFile:
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        header_line = handle.readline()
        if not header_line:
            raise ValueError(f"CSV is empty: {path}")

        fieldnames = next(csv.reader([header_line], delimiter=";"))
        if len(fieldnames) != len(set(fieldnames)):
            raise ValueError(f"CSV contains duplicate column names: {path}")

        records: list[CsvRecord] = []
        for line_number, raw_line in enumerate(handle, start=2):
            if not raw_line.strip():
                continue
            columns = next(csv.reader([raw_line], delimiter=";"))
            if len(columns) != len(fieldnames):
                raise ValueError(
                    f"Malformed row {line_number} in {path}: "
                    f"expected {len(fieldnames)} columns, got {len(columns)}"
                )
            records.append(CsvRecord(raw_line, dict(zip(fieldnames, columns))))

    return CsvFile(header_line, fieldnames, records)


def index_jps_rows(csv_file: CsvFile, path: Path, jps_only: bool) -> dict[str, CsvRecord]:
    indexed: dict[str, CsvRecord] = {}
    for record in csv_file.records:
        algorithm = record.values.get("Algorithm", "")
        if jps_only and algorithm != ALGORITHM_NAME:
            raise ValueError(
                f"JPS rerun contains algorithm {algorithm!r}; expected only {ALGORITHM_NAME}."
            )
        if algorithm != ALGORITHM_NAME:
            continue

        test_id = record.values.get("TestID", "")
        if not test_id:
            raise ValueError(f"JPS row without TestID in {path}")
        if test_id in indexed:
            raise ValueError(f"Duplicate JPS TestID={test_id} in {path}")
        indexed[test_id] = record

    return indexed


def validate_compatible(
    base: CsvFile,
    rerun: CsvFile,
    base_path: Path,
    rerun_path: Path,
) -> tuple[dict[str, CsvRecord], dict[str, CsvRecord]]:
    if base.fieldnames != rerun.fieldnames:
        raise ValueError(
            "CSV headers differ. The rerun must use exactly the same benchmark schema."
        )

    required = {"Algorithm", *INPUT_FIELDS, *LOGICAL_RESULT_FIELDS}
    missing = sorted(required.difference(base.fieldnames))
    if missing:
        raise ValueError(f"Required columns are missing: {', '.join(missing)}")

    base_jps = index_jps_rows(base, base_path, jps_only=False)
    new_jps = index_jps_rows(rerun, rerun_path, jps_only=True)
    if not base_jps:
        raise ValueError("The base CSV contains no JumpPointSearch rows.")

    base_ids = set(base_jps)
    new_ids = set(new_jps)
    missing_ids = sorted(base_ids - new_ids, key=int)
    extra_ids = sorted(new_ids - base_ids, key=int)
    if missing_ids or extra_ids:
        details = []
        if missing_ids:
            details.append(f"missing {len(missing_ids)} TestID (first: {missing_ids[:5]})")
        if extra_ids:
            details.append(f"extra {len(extra_ids)} TestID (first: {extra_ids[:5]})")
        raise ValueError("JPS TestID sets differ: " + "; ".join(details))

    compared_fields = INPUT_FIELDS + LOGICAL_RESULT_FIELDS
    for test_id in sorted(base_ids, key=int):
        old = base_jps[test_id].values
        new = new_jps[test_id].values
        for field in compared_fields:
            if old[field] != new[field]:
                raise ValueError(
                    f"TestID={test_id} differs in {field}: "
                    f"base={old[field]!r}, rerun={new[field]!r}. Merge aborted."
                )

    return base_jps, new_jps


def merge(base_path: Path, rerun_path: Path, output_path: Path, force: bool) -> int:
    base_path = base_path.resolve()
    rerun_path = rerun_path.resolve()
    output_path = output_path.resolve()

    if output_path in (base_path, rerun_path):
        raise ValueError("Output must be a new file; input CSV files are never overwritten.")
    if output_path.exists() and not force:
        raise FileExistsError(f"Output already exists (use --force to replace it): {output_path}")

    base = read_csv_file(base_path)
    rerun = read_csv_file(rerun_path)
    _, new_jps = validate_compatible(base, rerun, base_path, rerun_path)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path = output_path.with_name(output_path.name + ".tmp")
    if temporary_path.exists():
        temporary_path.unlink()

    replaced = 0
    try:
        with temporary_path.open("w", encoding="utf-8", newline="") as handle:
            handle.write(base.header_line.lstrip("\ufeff"))
            for record in base.records:
                if record.values["Algorithm"] == ALGORITHM_NAME:
                    handle.write(new_jps[record.values["TestID"]].raw_line)
                    replaced += 1
                else:
                    handle.write(record.raw_line)
        os.replace(temporary_path, output_path)
    finally:
        if temporary_path.exists():
            temporary_path.unlink()

    return replaced


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Replace JPS rows in a complete benchmark CSV with a validated JPS-only rerun."
    )
    parser.add_argument("--base", required=True, type=Path, help="Complete five-algorithm CSV.")
    parser.add_argument("--jps", required=True, type=Path, help="New JPS-only full-suite CSV.")
    parser.add_argument("--output", required=True, type=Path, help="New merged CSV path.")
    parser.add_argument("--force", action="store_true", help="Allow replacing an existing output file.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    try:
        replaced = merge(args.base, args.jps, args.output, args.force)
    except (OSError, ValueError) as exc:
        raise SystemExit(f"Merge failed: {exc}") from exc

    print(f"Merged JPS rows: {replaced:,}")
    print(f"Output: {args.output.resolve()}")


if __name__ == "__main__":
    main()
