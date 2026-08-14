#!/usr/bin/env python3
"""
Generate thesis-ready plots and summary tables for Guided By Flame pathfinding
benchmark CSV files.

The script intentionally avoids pandas so it can run in the current project
environment with only numpy + matplotlib.

Usage:
    python analyze_benchmark.py
    python analyze_benchmark.py --csv "../../../benchmark_results_official.csv"
    python analyze_benchmark.py --output "./outputs_official"
"""

from __future__ import annotations

import argparse
import csv
import _thread
import math
import statistics
import sys
import types
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable

try:
    # Some local Python setups expose six as a single module but do not register
    # six.moves as an importable module. python-dateutil imports six.moves while
    # matplotlib starts, so register a tiny compatibility module if needed.
    import six  # type: ignore

    if "six.moves" not in sys.modules and hasattr(six, "moves"):
        moves_module = types.ModuleType("six.moves")
        moves_module._thread = _thread
        moves_module.range = range
        sys.modules["six.moves"] = moves_module

    import matplotlib.pyplot as plt
    import numpy as np
except ModuleNotFoundError as exc:
    raise SystemExit(
        "Missing plotting dependency. Install it with:\n"
        "  python -m pip install matplotlib numpy\n"
        f"Original error: {exc}"
    )


ALGORITHMS = [
    "AStar",
    "Dijkstra",
    "GreedyBestFirst",
    "CustomGreedy",
    "JumpPointSearch",
]

SCENARIOS = [
    "Static",
    "DS1_MovingObstacles",
    "DS2_PathObstruction",
    "DS3_EscapingTarget",
]

DYNAMIC_SCENARIOS = [
    "DS1_MovingObstacles",
    "DS2_PathObstruction",
    "DS3_EscapingTarget",
]

TOPOLOGIES = [
    "OpenField",
    "Maze",
    "RoomCorridor",
    "ScatteredBlock",
]

SCENARIO_LABELS = {
    "Static": "Scenariusz statyczny",
    "DS1_MovingObstacles": "Scenariusz z ruchomymi przeszkodami",
    "DS2_PathObstruction": "Scenariusz z blokowaniem ścieżki",
    "DS3_EscapingTarget": "Scenariusz z uciekającym celem",
}

TOPOLOGY_LABELS = {
    "OpenField": "Otwarta przestrzeń",
    "Maze": "Labirynt",
    "RoomCorridor": "Pokoje i korytarze",
    "ScatteredBlock": "Rozproszone bloki",
}

TOPOLOGY_SLUGS = {
    "OpenField": "otwarta_przestrzen",
    "Maze": "labirynt",
    "RoomCorridor": "pokoje_i_korytarze",
    "ScatteredBlock": "rozproszone_bloki",
}

ALGORITHM_COLORS = {
    "AStar": "#2f6fbb",
    "Dijkstra": "#b23a48",
    "GreedyBestFirst": "#2f9c67",
    "CustomGreedy": "#8a5cc2",
    "JumpPointSearch": "#d88922",
}

# -1 oznacza brak odczytu w benchmarku. Wartości powyżej 110°C są traktowane
# jako błędny odczyt sensora i nie mogą wpływać na średnie ani wykresy.
MIN_VALID_CPU_TEMPERATURE = 0.0
MAX_VALID_CPU_TEMPERATURE = 110.0


@dataclass
class BenchmarkRow:
    test_id: int
    algorithm: str
    start_x: int
    start_y: int
    target_x: int
    target_y: int
    scenario: str
    topology: str
    map_seed: int
    map_density: float
    map_width: int
    map_height: int
    distance_bucket: str
    path_found: bool
    cold_start_ms: float
    avg_ms: float
    min_ms: float
    max_ms: float
    std_ms: float
    avg_ticks: float
    gc_bytes: float
    explored_nodes: float
    jump_scanned_cells: float
    path_length: float
    path_cost_10_14: float
    direction_changes: float
    path_smoothness: float
    replans: float
    cpu_temp: float
    reference_length: float
    reference_path_cost_10_14: float = math.nan

    @property
    def size_label(self) -> str:
        return f"{self.map_width}x{self.map_height}"

    @property
    def reference_key(self) -> tuple:
        """Identify the same initial map and start-target pair across scenarios."""
        density = round(self.map_density, 6) if math.isfinite(self.map_density) else None
        return (
            self.topology,
            self.map_seed,
            density,
            self.map_width,
            self.map_height,
            self.start_x,
            self.start_y,
            self.target_x,
            self.target_y,
        )

    @property
    def path_ratio(self) -> float | None:
        # Statyczna referencja prowadzi do pierwotnej pozycji celu i nie jest
        # poprawnym mianownikiem jakości dla scenariusza z uciekającym celem.
        if self.scenario == "DS3_EscapingTarget":
            return None
        if not self.path_found or self.reference_length <= 0 or self.path_length <= 0:
            return None
        return self.path_length / self.reference_length

    @property
    def path_cost_ratio(self) -> float | None:
        """Return actual 10/14 cost divided by the initial static optimum."""
        if self.scenario == "DS3_EscapingTarget":
            return None
        if (
            not self.path_found
            or not math.isfinite(self.path_cost_10_14)
            or self.path_cost_10_14 <= 0
            or not math.isfinite(self.reference_path_cost_10_14)
            or self.reference_path_cost_10_14 <= 0
        ):
            return None
        return self.path_cost_10_14 / self.reference_path_cost_10_14


def safe_float(value: str, default: float = math.nan) -> float:
    if value is None:
        return default
    value = value.strip().replace(",", ".")
    if value == "":
        return default
    try:
        return float(value)
    except ValueError:
        return default


def safe_int(value: str, default: int = -1) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return default


def safe_bool(value: str) -> bool:
    return str(value).strip().lower() == "true"


def load_rows(csv_path: Path) -> tuple[list[BenchmarkRow], int]:
    rows: list[BenchmarkRow] = []
    skipped = 0

    with csv_path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle, delimiter=";")
        for raw in reader:
            required = [
                "TestID",
                "Algorithm",
                "Scenario",
                "MapTopology",
                "MapWidth",
                "MapHeight",
                "AvgExecutionTimeMs",
                "PathFound",
            ]
            if any(raw.get(field, "").strip() == "" for field in required):
                skipped += 1
                continue

            rows.append(
                BenchmarkRow(
                    test_id=safe_int(raw.get("TestID", "")),
                    algorithm=raw.get("Algorithm", ""),
                    start_x=safe_int(raw.get("StartX", "")),
                    start_y=safe_int(raw.get("StartY", "")),
                    target_x=safe_int(raw.get("TargetX", "")),
                    target_y=safe_int(raw.get("TargetY", "")),
                    scenario=raw.get("Scenario", ""),
                    topology=raw.get("MapTopology", ""),
                    map_seed=safe_int(raw.get("MapSeed", "")),
                    map_density=safe_float(raw.get("MapDensity", "")),
                    map_width=safe_int(raw.get("MapWidth", "")),
                    map_height=safe_int(raw.get("MapHeight", "")),
                    distance_bucket=raw.get("DistanceBucket", "Unknown"),
                    path_found=safe_bool(raw.get("PathFound", "")),
                    cold_start_ms=safe_float(raw.get("ColdStartTimeMs", "")),
                    avg_ms=safe_float(raw.get("AvgExecutionTimeMs", "")),
                    min_ms=safe_float(raw.get("MinExecutionTimeMs", "")),
                    max_ms=safe_float(raw.get("MaxExecutionTimeMs", "")),
                    std_ms=safe_float(raw.get("StdDevExecutionTimeMs", "")),
                    avg_ticks=safe_float(raw.get("AvgExecutionTicks", "")),
                    gc_bytes=safe_float(raw.get("AvgGCAllocBytes", "")),
                    explored_nodes=safe_float(raw.get("ExploredNodes", "")),
                    jump_scanned_cells=safe_float(raw.get("JumpScannedCells", "")),
                    path_length=safe_float(raw.get("PathLength", "")),
                    path_cost_10_14=safe_float(raw.get("PathCost10_14", "")),
                    direction_changes=safe_float(raw.get("DirectionChanges", "")),
                    path_smoothness=safe_float(raw.get("PathSmoothness", "")),
                    replans=safe_float(raw.get("PathRecalculations", "")),
                    cpu_temp=safe_float(raw.get("CPUTemperature", "")),
                    reference_length=safe_float(raw.get("ReferenceShortestPathLength", "")),
                )
            )

    return rows, skipped


def assign_reference_path_costs(rows: list[BenchmarkRow]) -> tuple[int, int]:
    """
    Attach the canonical 10/14 reference cost to every matching scenario row.

    The initial static Dijkstra result is the reference because it uses the same
    movement costs (10 orthogonally, 14 diagonally) as PathCost10_14. DS3 is not
    comparable with this initial target reference because its target moves.
    """
    references: dict[tuple, float] = {}
    for row in rows:
        if (
            row.scenario != "Static"
            or row.algorithm != "Dijkstra"
            or not row.path_found
            or not math.isfinite(row.path_cost_10_14)
            or row.path_cost_10_14 <= 0
        ):
            continue

        existing = references.get(row.reference_key)
        if existing is not None and not math.isclose(existing, row.path_cost_10_14):
            raise ValueError(
                "Conflicting static Dijkstra reference costs for "
                f"map/pair key {row.reference_key}: {existing} vs {row.path_cost_10_14}"
            )
        references[row.reference_key] = row.path_cost_10_14

    missing = 0
    for row in rows:
        row.reference_path_cost_10_14 = references.get(row.reference_key, math.nan)
        if (
            row.scenario != "DS3_EscapingTarget"
            and row.path_found
            and math.isfinite(row.path_cost_10_14)
            and row.path_cost_10_14 > 0
            and not math.isfinite(row.reference_path_cost_10_14)
        ):
            missing += 1

    return len(references), missing


def finite(values: Iterable[float]) -> list[float]:
    return [value for value in values if value is not None and math.isfinite(value)]


def valid_cpu_temperature(value: float) -> bool:
    return (
        math.isfinite(value)
        and MIN_VALID_CPU_TEMPERATURE <= value <= MAX_VALID_CPU_TEMPERATURE
    )


def mean(values: Iterable[float]) -> float:
    vals = finite(values)
    return float(statistics.fmean(vals)) if vals else math.nan


def median(values: Iterable[float]) -> float:
    vals = finite(values)
    return float(statistics.median(vals)) if vals else math.nan


def percentile(values: Iterable[float], p: float) -> float:
    vals = finite(values)
    if not vals:
        return math.nan
    return float(np.percentile(np.asarray(vals, dtype=float), p))


def group_rows(rows: list[BenchmarkRow], key_func: Callable[[BenchmarkRow], tuple]) -> dict[tuple, list[BenchmarkRow]]:
    grouped: dict[tuple, list[BenchmarkRow]] = defaultdict(list)
    for row in rows:
        grouped[key_func(row)].append(row)
    return grouped


def setup_style() -> None:
    plt.rcParams.update(
        {
            "figure.dpi": 130,
            "savefig.dpi": 300,
            "font.family": "DejaVu Sans",
            "font.size": 10,
            "axes.titlesize": 12,
            "axes.labelsize": 10,
            "xtick.labelsize": 9,
            "ytick.labelsize": 9,
            "legend.fontsize": 9,
            "axes.grid": True,
            "grid.alpha": 0.22,
            "grid.linestyle": "-",
            "axes.spines.top": False,
            "axes.spines.right": False,
        }
    )


def scenario_label(scenario: str) -> str:
    """Return a thesis-friendly Polish scenario name."""
    return SCENARIO_LABELS.get(scenario, scenario)


def topology_label(topology: str) -> str:
    """Return a thesis-friendly Polish map topology name."""
    return TOPOLOGY_LABELS.get(topology, topology)


def show_y_axis_on_all(axes: Iterable[plt.Axes]) -> None:
    """Keep Y tick values visible on every panel, also with sharey=True."""
    for ax in axes:
        ax.tick_params(axis="y", which="both", labelleft=True)


def save_figure(fig: plt.Figure, output_dir: Path, name: str) -> None:
    fig.tight_layout()
    fig.savefig(output_dir / f"{name}.png", bbox_inches="tight")
    fig.savefig(output_dir / f"{name}.pdf", bbox_inches="tight")
    plt.close(fig)


def write_csv(path: Path, headers: list[str], rows: Iterable[list]) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, delimiter=";")
        writer.writerow(headers)
        writer.writerows(rows)


def write_summary_tables(rows: list[BenchmarkRow], output_dir: Path, skipped: int) -> None:
    summary_rows = []
    grouped = group_rows(rows, lambda r: (r.scenario, r.algorithm))
    for scenario in SCENARIOS:
        for algorithm in ALGORITHMS:
            group = grouped.get((scenario, algorithm), [])
            if not group:
                continue
            path_ratios = [r.path_ratio for r in group if r.path_ratio is not None]
            path_cost_ratios = [r.path_cost_ratio for r in group if r.path_cost_ratio is not None]
            completed_replans = [r.replans for r in group if r.path_found]
            summary_rows.append(
                [
                    scenario,
                    algorithm,
                    len(group),
                    f"{100.0 * sum(r.path_found for r in group) / len(group):.3f}",
                    f"{mean(r.avg_ms for r in group):.6f}",
                    f"{median(r.avg_ms for r in group):.6f}",
                    f"{percentile((r.avg_ms for r in group), 95):.6f}",
                    f"{mean(r.cold_start_ms for r in group):.6f}",
                    f"{mean(r.explored_nodes for r in group):.3f}",
                    f"{mean(completed_replans):.3f}",
                    f"{median(completed_replans):.3f}",
                    f"{percentile(completed_replans, 95):.3f}",
                    f"{percentile(completed_replans, 99):.3f}",
                    f"{mean(path_ratios):.6f}",
                    f"{mean(path_cost_ratios):.6f}",
                    f"{mean(r.cpu_temp for r in group if valid_cpu_temperature(r.cpu_temp)):.3f}",
                ]
            )

    write_csv(
        output_dir / "summary_by_scenario_algorithm.csv",
        [
            "Scenario",
            "Algorithm",
            "Rows",
            "PathFoundPercent",
            "AvgExecutionTimeMsMean",
            "AvgExecutionTimeMsMedian",
            "AvgExecutionTimeMsP95",
            "ColdStartTimeMsMean",
            "ExploredNodesMean",
            "PathRecalculationsCompletedMean",
            "PathRecalculationsCompletedMedian",
            "PathRecalculationsCompletedP95",
            "PathRecalculationsCompletedP99",
            "PathLengthToReferenceMean",
            "PathCost10_14ToReferenceMean",
            "CPUTemperatureMean",
        ],
        summary_rows,
    )

    map_rows = []
    grouped_map = group_rows(rows, lambda r: (r.map_width, r.topology, r.map_density, r.scenario, r.algorithm))
    for key in sorted(grouped_map.keys()):
        group = grouped_map[key]
        completed_replans = [r.replans for r in group if r.path_found]
        path_cost_ratios = [r.path_cost_ratio for r in group if r.path_cost_ratio is not None]
        map_rows.append(
            [
                key[0],
                key[1],
                f"{key[2]:.2f}",
                key[3],
                key[4],
                len(group),
                f"{100.0 * sum(r.path_found for r in group) / len(group):.3f}",
                f"{mean(r.avg_ms for r in group):.6f}",
                f"{percentile((r.avg_ms for r in group), 95):.6f}",
                f"{mean(r.explored_nodes for r in group):.3f}",
                f"{mean(completed_replans):.3f}",
                f"{median(completed_replans):.3f}",
                f"{percentile(completed_replans, 95):.3f}",
                f"{percentile(completed_replans, 99):.3f}",
                f"{mean(path_cost_ratios):.6f}",
            ]
        )
    write_csv(
        output_dir / "summary_by_map_scenario_algorithm.csv",
        [
            "MapWidth",
            "MapTopology",
            "MapDensity",
            "Scenario",
            "Algorithm",
            "Rows",
            "PathFoundPercent",
            "AvgExecutionTimeMsMean",
            "AvgExecutionTimeMsP95",
            "ExploredNodesMean",
            "PathRecalculationsCompletedMean",
            "PathRecalculationsCompletedMedian",
            "PathRecalculationsCompletedP95",
            "PathRecalculationsCompletedP99",
            "PathCost10_14ToReferenceMean",
        ],
        map_rows,
    )

    with (output_dir / "analysis_report.md").open("w", encoding="utf-8") as handle:
        handle.write("# Analiza benchmarku Guided By Flame\n\n")
        handle.write(f"- Przeanalizowane wiersze: {len(rows):,}\n")
        handle.write(f"- Pominięte niekompletne wiersze: {skipped:,}\n")
        handle.write(f"- Unikatowe identyfikatory testów: {len(set(r.test_id for r in rows)):,}\n")
        handle.write(f"- Rozmiary map: {', '.join(sorted(set(r.size_label for r in rows)))}\n")
        handle.write(
            f"- Scenariusze: {', '.join(scenario_label(s) for s in SCENARIOS if any(r.scenario == s for r in rows))}\n"
        )
        handle.write(
            f"- Typy map: {', '.join(topology_label(t) for t in TOPOLOGIES if any(r.topology == t for r in rows))}\n"
        )
        valid_temperatures = [r.cpu_temp for r in rows if valid_cpu_temperature(r.cpu_temp)]
        temperature_range = (
            f"{min(valid_temperatures):.1f}–{max(valid_temperatures):.1f} °C"
            if valid_temperatures
            else "brak poprawnych odczytów"
        )
        handle.write(f"- Zakres poprawnych odczytów temperatury CPU: {temperature_range}\n")
        handle.write("\nWykresy zapisano w formatach PNG i PDF.\n")


def plot_grouped_bars_by_topology(
    rows: list[BenchmarkRow],
    output_dir: Path,
    metric: Callable[[BenchmarkRow], float | None],
    figure_title: str,
    ylabel: str,
    filename: str,
    scenarios: list[str] | None = None,
    log_scale: bool = True,
    reference_line: float | None = None,
) -> None:
    """Draw consistent grouped bars with map topology always on the X axis."""
    selected_scenarios = scenarios or SCENARIOS
    categories = list(TOPOLOGIES)
    labels = [topology_label(topology) for topology in TOPOLOGIES]

    if len(selected_scenarios) == 4:
        fig, axes_array = plt.subplots(2, 2, figsize=(12, 8), sharey=True)
        axes = list(axes_array.flat)
        title_y = 1.02
    else:
        fig, axes_array = plt.subplots(1, len(selected_scenarios), figsize=(14, 4.7), sharey=True)
        axes = list(np.atleast_1d(axes_array).flat)
        title_y = 1.05

    x = np.arange(len(categories))
    width = 0.15
    offsets = np.linspace(-2 * width, 2 * width, len(ALGORITHMS))
    for ax, scenario in zip(axes, selected_scenarios):
        for offset, algorithm in zip(offsets, ALGORITHMS):
            values = [
                mean(
                    metric(row)
                    for row in rows
                    if row.scenario == scenario
                    and row.algorithm == algorithm
                    and row.topology == category
                )
                for category in categories
            ]
            ax.bar(
                x + offset,
                values,
                width,
                label=algorithm,
                color=ALGORITHM_COLORS[algorithm],
                edgecolor="#333333",
                linewidth=0.2,
            )
        if reference_line is not None:
            ax.axhline(reference_line, color="#222222", linestyle="--", linewidth=1.1)
        ax.set_title(scenario_label(scenario))
        ax.set_xticks(x)
        ax.set_xticklabels(labels, rotation=20, ha="right")
        ax.set_ylabel(ylabel)
        if log_scale:
            ax.set_yscale("log")

    show_y_axis_on_all(axes)
    handles, legend_labels = axes[0].get_legend_handles_labels()
    fig.legend(
        handles,
        legend_labels,
        loc="lower center",
        bbox_to_anchor=(0.5, -0.025),
        ncol=len(ALGORITHMS),
        frameon=True,
    )
    fig.suptitle(figure_title, fontsize=15, y=title_y)
    save_figure(fig, output_dir, filename)


def plot_topology_bars_for_map_variants(
    rows: list[BenchmarkRow],
    output_dir: Path,
    metric: Callable[[BenchmarkRow], float | None],
    figure_title: str,
    ylabel: str,
    filename_prefix: str,
    scenarios: list[str] | None = None,
    log_scale: bool = True,
    reference_line: float | None = None,
) -> None:
    """Create topology figures for all data, every map size and every density."""
    plot_grouped_bars_by_topology(
        rows,
        output_dir,
        metric,
        f"{figure_title} - wszystkie rozmiary map",
        ylabel,
        f"{filename_prefix}_all_sizes",
        scenarios=scenarios,
        log_scale=log_scale,
        reference_line=reference_line,
    )

    sizes = sorted(set((row.map_width, row.map_height) for row in rows))
    for width, height in sizes:
        subset = [
            row
            for row in rows
            if row.map_width == width and row.map_height == height
        ]
        plot_grouped_bars_by_topology(
            subset,
            output_dir,
            metric,
            f"{figure_title} - rozmiar mapy: {width} × {height}",
            ylabel,
            f"{filename_prefix}_{width}x{height}",
            scenarios=scenarios,
            log_scale=log_scale,
            reference_line=reference_line,
        )

    densities = sorted(set(row.map_density for row in rows if math.isfinite(row.map_density)))
    for density in densities:
        subset = [
            row
            for row in rows
            if math.isclose(row.map_density, density, abs_tol=1e-9)
        ]
        density_percent = int(round(100.0 * density))
        plot_grouped_bars_by_topology(
            subset,
            output_dir,
            metric,
            f"{figure_title} - gęstość przeszkód: {density_percent}% - wszystkie rozmiary map",
            ylabel,
            f"{filename_prefix}_density_{density_percent}pct",
            scenarios=scenarios,
            log_scale=log_scale,
            reference_line=reference_line,
        )


def plot_execution_by_scenario(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: row.avg_ms,
        "Średni czas wykonania algorytmów według typu mapy",
        "Średni czas wykonania [ms] (skala log.)",
        "01_execution_time_by_topology",
    )


def plot_time_by_map_size(rows: list[BenchmarkRow], output_dir: Path) -> None:
    sizes = sorted(set(r.map_width for r in rows))
    fig, axes = plt.subplots(2, 2, figsize=(12, 8), sharey=True)
    for ax, scenario in zip(axes.flat, SCENARIOS):
        for algorithm in ALGORITHMS:
            y = [
                mean(r.avg_ms for r in rows if r.scenario == scenario and r.algorithm == algorithm and r.map_width == size)
                for size in sizes
            ]
            ax.plot(
                sizes,
                y,
                marker="o",
                linewidth=2,
                label=algorithm,
                color=ALGORITHM_COLORS[algorithm],
            )
        ax.set_title(scenario_label(scenario))
        ax.set_xlabel("Rozmiar mapy [liczba pól boku]")
        ax.set_xticks(sizes)
        ax.set_yscale("log")
        ax.set_ylabel("Średni czas wykonania [ms] (skala log.)")
    show_y_axis_on_all(axes.flat)
    axes.flat[0].legend(loc="upper left", frameon=True)
    fig.suptitle("Skalowanie czasu wykonania względem rozmiaru mapy", fontsize=15, y=1.02)
    save_figure(fig, output_dir, "02_execution_time_scaling_by_map_size")


def plot_explored_nodes(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: row.explored_nodes,
        "Liczba odwiedzonych węzłów według typu mapy",
        "Średnia liczba odwiedzonych węzłów (skala log.)",
        "03_explored_nodes_by_topology",
    )


def plot_path_quality(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: row.path_cost_ratio,
        "Jakość ścieżki według typu mapy",
        "Koszt ścieżki 10/14 / optymalny koszt referencyjny",
        "04_path_quality_by_topology",
        scenarios=["Static", "DS1_MovingObstacles", "DS2_PathObstruction"],
        log_scale=False,
        reference_line=1.0,
    )


def plot_path_costs(rows: list[BenchmarkRow], output_dir: Path) -> None:
    def valid_path_length(row: BenchmarkRow) -> float | None:
        if not row.path_found or not math.isfinite(row.path_length) or row.path_length <= 0:
            return None
        return row.path_length

    def excess_path_cost_percent(row: BenchmarkRow) -> float | None:
        ratio = row.path_cost_ratio
        if ratio is None:
            return None
        return 100.0 * (ratio - 1.0)

    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        valid_path_length,
        "Średnia długość ścieżki według typu mapy",
        "Średnia długość ścieżki [jednostki mapy] (skala log.)",
        "17_path_length_by_topology",
        log_scale=True,
    )
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        excess_path_cost_percent,
        "Nadmiarowy koszt ścieżki 10/14 według typu mapy",
        "Nadmiarowy koszt 10/14 [%]",
        "18_excess_path_cost_by_topology",
        scenarios=["Static", "DS1_MovingObstacles", "DS2_PathObstruction"],
        log_scale=False,
        reference_line=0.0,
    )


def plot_replanning(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: row.replans if row.path_found else None,
        "Ponowne wyznaczanie ścieżki w ukończonych przebiegach",
        "Średnia liczba ponownych wyznaczeń (ukończone przebiegi)",
        "05_dynamic_replanning_by_topology",
        scenarios=DYNAMIC_SCENARIOS,
        log_scale=False,
    )


def plot_dynamic_completion_rate(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: 100.0 if row.path_found else 0.0,
        "Odsetek ukończonych przebiegów dynamicznych według typu mapy",
        "Ukończone przebiegi [%]",
        "19_dynamic_completion_rate_by_topology",
        scenarios=DYNAMIC_SCENARIOS,
        log_scale=False,
        reference_line=100.0,
    )


def plot_path_found_heatmap(rows: list[BenchmarkRow], output_dir: Path) -> None:
    matrix = np.zeros((len(SCENARIOS), len(ALGORITHMS)), dtype=float)
    for i, scenario in enumerate(SCENARIOS):
        for j, algorithm in enumerate(ALGORITHMS):
            subset = [r for r in rows if r.scenario == scenario and r.algorithm == algorithm]
            matrix[i, j] = 100.0 * sum(r.path_found for r in subset) / len(subset) if subset else math.nan

    fig, ax = plt.subplots(figsize=(10, 5))
    im = ax.imshow(matrix, cmap="YlGnBu", vmin=0, vmax=100, aspect="auto")
    ax.set_xticks(np.arange(len(ALGORITHMS)))
    ax.set_xticklabels(ALGORITHMS, rotation=25, ha="right")
    ax.set_yticks(np.arange(len(SCENARIOS)))
    ax.set_yticklabels([scenario_label(scenario) for scenario in SCENARIOS])
    for i in range(matrix.shape[0]):
        for j in range(matrix.shape[1]):
            ax.text(j, i, f"{matrix[i, j]:.1f}%", ha="center", va="center", color="#111111")
    ax.set_title("Odsetek znalezionych ścieżek")
    fig.colorbar(im, ax=ax, label="Znalezione ścieżki [%]")
    save_figure(fig, output_dir, "06_path_found_rate_heatmap")


def plot_temperature(rows: list[BenchmarkRow], output_dir: Path) -> None:
    def valid_temperature(row: BenchmarkRow) -> float | None:
        if not valid_cpu_temperature(row.cpu_temp):
            return None
        return row.cpu_temp

    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        valid_temperature,
        "Średnia temperatura CPU według typu mapy",
        "Średnia temperatura CPU [°C]",
        "07_cpu_temperature_by_topology",
        log_scale=False,
        reference_line=95.0,
    )


def plot_cold_vs_warm(rows: list[BenchmarkRow], output_dir: Path) -> None:
    def cold_start_overhead(row: BenchmarkRow) -> float | None:
        if not math.isfinite(row.avg_ms) or row.avg_ms <= 0:
            return None
        return row.cold_start_ms / row.avg_ms

    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        cold_start_overhead,
        "Narzut pierwszego uruchomienia według typu mapy",
        "Pierwsze uruchomienie / średnia po rozgrzaniu",
        "08_cold_start_overhead_by_topology",
        log_scale=True,
        reference_line=1.0,
    )


def plot_gc_allocation_figures(rows: list[BenchmarkRow], output_dir: Path) -> None:
    """Create allocation figures with topology on X for every map size."""
    if not any(math.isfinite(row.gc_bytes) and row.gc_bytes > 0 for row in rows):
        print("GC allocation plots skipped: the CSV contains no positive allocation measurements.")
        return

    plot_topology_bars_for_map_variants(
        rows,
        output_dir,
        lambda row: row.gc_bytes if math.isfinite(row.gc_bytes) and row.gc_bytes > 0 else None,
        "Średnia alokacja pamięci według typu mapy",
        "Średnia alokacja pamięci [B] (skala log.)",
        "16_gc_allocation_by_topology",
    )


def plot_efficiency_tradeoff(rows: list[BenchmarkRow], output_dir: Path) -> None:
    comparable_scenarios = ["DS1_MovingObstacles", "DS2_PathObstruction"]
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.7), sharey=True)
    for ax, scenario in zip(axes, comparable_scenarios):
        for algorithm in ALGORITHMS:
            subset = [
                r
                for r in rows
                if r.scenario == scenario
                and r.algorithm == algorithm
                and r.path_cost_ratio is not None
            ]
            ax.scatter(
                mean(r.avg_ms for r in subset),
                mean(r.path_cost_ratio for r in subset if r.path_cost_ratio is not None),
                s=max(45, math.sqrt(len(subset))),
                color=ALGORITHM_COLORS[algorithm],
                label=algorithm,
                edgecolor="#222222",
                linewidth=0.6,
                alpha=0.9,
            )
        ax.axhline(1.0, color="#222222", linestyle="--", linewidth=1)
        ax.set_xscale("log")
        ax.set_title(scenario_label(scenario))
        ax.set_xlabel("Średni czas wykonania [ms] (skala log.)")
        ax.set_ylabel("Koszt ścieżki 10/14 / optymalny koszt referencyjny")
    show_y_axis_on_all(axes)
    axes[0].legend(frameon=True)
    fig.suptitle("Kompromis między szybkością algorytmu a jakością ścieżki", fontsize=15, y=1.05)
    save_figure(fig, output_dir, "10_speed_quality_tradeoff_dynamic")


def plot_scaling_for_each_topology(rows: list[BenchmarkRow], output_dir: Path) -> None:
    """Show size scaling separately for each kind of map."""
    sizes = sorted(set(row.map_width for row in rows))
    for topology in TOPOLOGIES:
        topology_rows = [row for row in rows if row.topology == topology]
        if not topology_rows:
            continue
        fig, axes = plt.subplots(2, 2, figsize=(12, 8), sharey=True)
        for ax, scenario in zip(axes.flat, SCENARIOS):
            for algorithm in ALGORITHMS:
                values = [
                    mean(
                        row.avg_ms
                        for row in topology_rows
                        if row.scenario == scenario and row.algorithm == algorithm and row.map_width == size
                    )
                    for size in sizes
                ]
                ax.plot(
                    sizes,
                    values,
                    marker="o",
                    linewidth=2,
                    label=algorithm,
                    color=ALGORITHM_COLORS[algorithm],
                )
            ax.set_title(scenario_label(scenario))
            ax.set_xlabel("Rozmiar mapy [liczba pól boku]")
            ax.set_xticks(sizes)
            ax.set_yscale("log")
            ax.set_ylabel("Średni czas wykonania [ms] (skala log.)")
        show_y_axis_on_all(axes.flat)
        axes.flat[0].legend(loc="upper left", frameon=True)
        fig.suptitle(
            f"Skalowanie czasu względem rozmiaru - typ mapy: {topology_label(topology)}",
            fontsize=15,
            y=1.02,
        )
        save_figure(fig, output_dir, f"13_scaling_topology_{TOPOLOGY_SLUGS[topology]}")


def plot_density_for_each_topology(rows: list[BenchmarkRow], output_dir: Path) -> None:
    """Show the obstacle-density effect separately for each kind of map."""
    densities = sorted(set(row.map_density for row in rows if math.isfinite(row.map_density)))
    density_percent = [100.0 * density for density in densities]
    for topology in TOPOLOGIES:
        topology_rows = [row for row in rows if row.topology == topology]
        if not topology_rows:
            continue
        fig, axes = plt.subplots(2, 2, figsize=(12, 8), sharey=True)
        for ax, scenario in zip(axes.flat, SCENARIOS):
            for algorithm in ALGORITHMS:
                values = [
                    mean(
                        row.avg_ms
                        for row in topology_rows
                        if row.scenario == scenario
                        and row.algorithm == algorithm
                        and math.isclose(row.map_density, density, abs_tol=1e-9)
                    )
                    for density in densities
                ]
                ax.plot(
                    density_percent,
                    values,
                    marker="o",
                    linewidth=2,
                    label=algorithm,
                    color=ALGORITHM_COLORS[algorithm],
                )
            ax.set_title(scenario_label(scenario))
            ax.set_xlabel("Gęstość przeszkód [%]")
            ax.set_xticks(density_percent)
            ax.set_yscale("log")
            ax.set_ylabel("Średni czas wykonania [ms] (skala log.)")
        show_y_axis_on_all(axes.flat)
        axes.flat[0].legend(loc="upper left", frameon=True)
        fig.suptitle(
            f"Wpływ gęstości przeszkód na czas - typ mapy: {topology_label(topology)}",
            fontsize=15,
            y=1.02,
        )
        save_figure(fig, output_dir, f"14_density_topology_{TOPOLOGY_SLUGS[topology]}")


def plot_all(rows: list[BenchmarkRow], output_dir: Path) -> None:
    plot_execution_by_scenario(rows, output_dir)
    plot_time_by_map_size(rows, output_dir)
    plot_explored_nodes(rows, output_dir)
    plot_path_quality(rows, output_dir)
    plot_path_costs(rows, output_dir)
    plot_replanning(rows, output_dir)
    plot_dynamic_completion_rate(rows, output_dir)
    plot_path_found_heatmap(rows, output_dir)
    plot_temperature(rows, output_dir)
    plot_cold_vs_warm(rows, output_dir)
    plot_efficiency_tradeoff(rows, output_dir)
    plot_scaling_for_each_topology(rows, output_dir)
    plot_density_for_each_topology(rows, output_dir)
    plot_gc_allocation_figures(rows, output_dir)


def default_project_csv() -> Path:
    script_path = Path(__file__).resolve()
    project_root = script_path.parents[4]
    return project_root / "benchmark_results_new_final_jj.csv"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Analyze Guided By Flame pathfinding benchmark CSV.")
    parser.add_argument("--csv", type=Path, default=default_project_csv(), help="Path to benchmark CSV file.")
    parser.add_argument(
        "--output",
        type=Path,
        default=Path(__file__).resolve().parent / "outputs",
        help="Directory for plots and summary tables.",
    )
    parser.add_argument(
        "--plots-only",
        action="store_true",
        help="Generate plots without overwriting summary CSV and report files.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    csv_path = args.csv.resolve()
    output_dir = args.output.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    if not csv_path.exists():
        raise SystemExit(f"CSV file not found: {csv_path}")

    setup_style()
    rows, skipped = load_rows(csv_path)
    if not rows:
        raise SystemExit("No complete benchmark rows found.")

    reference_cost_count, missing_reference_costs = assign_reference_path_costs(rows)
    if reference_cost_count == 0:
        print(
            "WARNING: no static Dijkstra PathCost10_14 references were found; "
            "cost-quality plots will contain no data."
        )
    elif missing_reference_costs:
        print(
            "WARNING: "
            f"{missing_reference_costs:,} comparable rows have no matching static Dijkstra cost."
        )

    if not args.plots_only:
        write_summary_tables(rows, output_dir, skipped)
    plot_all(rows, output_dir)

    print(f"Analysed rows: {len(rows):,}")
    print(f"Skipped incomplete rows: {skipped:,}")
    print(f"Static Dijkstra 10/14 references: {reference_cost_count:,}")
    print(f"Comparable rows without a 10/14 reference: {missing_reference_costs:,}")
    if args.plots_only:
        print("Summary tables: skipped (--plots-only)")
    print(f"Output directory: {output_dir}")


if __name__ == "__main__":
    main()
