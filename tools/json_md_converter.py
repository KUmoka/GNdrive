#!/usr/bin/env python3
"""Convert character data between JSON and Markdown using a mapping CSV."""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


HEADING_RE = re.compile(r"^(#{1,6})\s*(.*)$")
BULLET_RE = re.compile(r"^(\s*)-\s*(.*)$")
LIST_PATHS = {
    "alias",
    "role",
    "features.gender",
    "features.traits",
    "features.skills",
    "features.remarks",
    "features.likes",
    "features.dislikes",
    "living.features",
    "hobbies",
    "ship_life",
}
DICT_PATHS = {"personality", "relationship", "quotes"}


@dataclass(frozen=True)
class MappingEntry:
    source_path: str
    json_path: str
    heading: str
    level: int
    title: str


def load_mapping(path: Path) -> list[MappingEntry]:
    rows: list[tuple[str, str, int, str]] = []
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        for row in csv.reader(handle):
            if len(row) < 2:
                continue
            source_path = clean_cell(row[0])
            heading = clean_cell(row[1])
            if not source_path or not heading.startswith("#"):
                continue
            match = HEADING_RE.match(heading)
            if not match:
                continue
            rows.append((source_path, heading, len(match.group(1)), match.group(2).strip()))

    entries: list[MappingEntry] = []
    stack: list[tuple[int, str]] = []
    for source_path, heading, level, title in rows:
        while stack and stack[-1][0] >= level:
            stack.pop()
        parent_path = stack[-1][1] if stack else ""
        json_path = infer_json_path(source_path, parent_path)
        entries.append(MappingEntry(source_path, json_path, heading, level, title))
        if heading != "#":
            stack.append((level, json_path))
    return entries


def clean_cell(value: str) -> str:
    value = value.strip()
    while len(value) >= 2 and value[0] == value[-1] == '"':
        value = value[1:-1].strip()
    return value


def infer_json_path(source_path: str, parent_path: str) -> str:
    if not parent_path:
        return source_path
    if source_path == parent_path or source_path.startswith(parent_path + "."):
        return source_path
    return parent_path + "." + source_path.rsplit(".", 1)[-1]


def get_path(data: Any, dotted_path: str) -> Any:
    current = data
    for part in dotted_path.split("."):
        if not isinstance(current, dict) or part not in current:
            return None
        current = current[part]
    return current


def set_path(data: dict[str, Any], dotted_path: str, value: Any) -> None:
    current = data
    parts = dotted_path.split(".")
    for part in parts[:-1]:
        child = current.get(part)
        if not isinstance(child, dict):
            child = {}
            current[part] = child
        current = child
    current[parts[-1]] = value


def has_mapped_children(entry: MappingEntry, entries: list[MappingEntry]) -> bool:
    prefix = entry.json_path + "."
    return any(other.json_path.startswith(prefix) for other in entries)


def json_to_markdown(data: dict[str, Any], entries: list[MappingEntry]) -> str:
    lines: list[str] = []
    for entry in entries:
        value = get_path(data, entry.json_path)
        if value is None and entry.level != 1:
            continue

        if entry.level == 1 and entry.heading == "#":
            lines.append(f"# {scalar_to_text(value)}")
            continue

        lines.append(entry.heading)
        if isinstance(value, dict) and has_mapped_children(entry, entries):
            continue
        lines.extend(format_value(value, indent=0))

    return "\n".join(lines).rstrip() + "\n"


def format_value(value: Any, indent: int) -> list[str]:
    pad = " " * indent
    if value is None:
        return [f"{pad}- null"]
    if isinstance(value, list):
        lines: list[str] = []
        for item in value:
            if isinstance(item, dict):
                first_key = preferred_first_key(item)
                lines.append(f"{pad}- {scalar_to_text(item.get(first_key))}")
                for key, child in item.items():
                    if key == first_key:
                        continue
                    child_lines = format_value(child, indent + 4)
                    if len(child_lines) == 1 and child_lines[0].lstrip().startswith("- "):
                        lines.append(f"{' ' * (indent + 4)}- {key}: {child_lines[0].lstrip()[2:]}")
                    else:
                        lines.append(f"{' ' * (indent + 4)}- {key}")
                        lines.extend(child_lines)
            else:
                lines.append(f"{pad}- {scalar_to_text(item)}")
        return lines
    if isinstance(value, dict):
        lines = []
        for key, child in value.items():
            if is_scalar(child):
                lines.append(f"{pad}- {key}: {scalar_to_text(child)}")
            else:
                lines.append(f"{pad}- {key}")
                lines.extend(format_value(child, indent + 4))
        return lines
    return [f"{pad}- {scalar_to_text(value)}"]


def preferred_first_key(item: dict[str, Any]) -> str:
    for key in ("date", "event", "name", "id"):
        if key in item:
            return key
    return next(iter(item), "")


def is_scalar(value: Any) -> bool:
    return value is None or isinstance(value, (str, int, float, bool))


def scalar_to_text(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def markdown_to_json(text: str, entries: list[MappingEntry]) -> dict[str, Any]:
    title_to_entries = {(entry.level, entry.title): entry for entry in entries}
    result: dict[str, Any] = {}
    current_entry: MappingEntry | None = None
    current_lines: list[str] = []

    def flush() -> None:
        nonlocal current_entry, current_lines
        if current_entry is None or has_mapped_children(current_entry, entries):
            current_lines = []
            return
        value = parse_block(current_lines, current_entry)
        set_path(result, current_entry.json_path, value)
        current_lines = []

    for line in text.splitlines():
        match = HEADING_RE.match(line)
        if match:
            flush()
            level = len(match.group(1))
            title = match.group(2).strip()
            current_entry = title_to_entries.get((level, title))
            if level == 1 and current_entry and current_entry.heading == "#":
                set_path(result, current_entry.json_path, parse_scalar(title))
                current_entry = None
            continue
        if current_entry is not None:
            current_lines.append(line)
    flush()
    return result


def parse_block(lines: list[str], entry: MappingEntry) -> Any:
    bullets = parse_bullets(lines)
    if not bullets:
        return None
    if is_list_path(entry.json_path):
        return [parse_node(text, children) for text, children in bullets]
    if is_dict_path(entry.json_path) and all(not children and ":" in text for text, children in bullets):
        return dict(parse_key_value(text) for text, _ in bullets)
    if len(bullets) == 1 and not bullets[0][1]:
        return parse_scalar(bullets[0][0])
    if all(children and len(children) == 1 for _, children in bullets):
        if entry.json_path.endswith("memories"):
            return [
                {"date": parse_scalar(key), "event": parse_child_value(children[0][0])}
                for key, children in bullets
            ]
        return {key: parse_scalar(children[0][0]) for key, children in bullets}
    return [parse_node(text, children) for text, children in bullets]


def is_list_path(json_path: str) -> bool:
    return json_path in LIST_PATHS


def is_dict_path(json_path: str) -> bool:
    return json_path in DICT_PATHS


def parse_key_value(text: str) -> tuple[str, Any]:
    key, value = text.split(":", 1)
    return key.strip(), parse_scalar(value.strip())


def parse_child_value(text: str) -> Any:
    if ":" in text:
        return parse_key_value(text)[1]
    return parse_scalar(text)


def parse_bullets(lines: list[str]) -> list[tuple[str, list[tuple[str, list[Any]]]]]:
    root: list[tuple[int, str, list[Any]]] = []
    stack: list[tuple[int, str, list[Any]]] = []
    for line in lines:
        match = BULLET_RE.match(line)
        if not match:
            continue
        indent = len(match.group(1))
        node = (indent, match.group(2).strip(), [])
        while stack and stack[-1][0] >= indent:
            stack.pop()
        if stack:
            stack[-1][2].append(node)
        else:
            root.append(node)
        stack.append(node)
    return [(text, strip_indent(children)) for _, text, children in root]


def strip_indent(nodes: list[tuple[int, str, list[Any]]]) -> list[tuple[str, list[Any]]]:
    return [(text, strip_indent(children)) for _, text, children in nodes]


def parse_node(text: str, children: list[tuple[str, list[Any]]]) -> Any:
    if children:
        if all(not child_children and ":" in child_text for child_text, child_children in children):
            item: dict[str, Any] = {}
            for child_text, _ in children:
                key, value = child_text.split(":", 1)
                item[key.strip()] = parse_scalar(value.strip())
            if text:
                first_key = "date" if "date" not in item else "value"
                item = {first_key: parse_scalar(text), **item}
            return item
        return {text: [parse_node(child_text, child_children) for child_text, child_children in children]}
    return parse_scalar(text)


def parse_scalar(text: str) -> Any:
    if text == "null":
        return None
    if text == "true":
        return True
    if text == "false":
        return False
    if re.fullmatch(r"-?\d+", text):
        return int(text)
    if re.fullmatch(r"-?\d+\.\d+", text):
        return float(text)
    return text


def choose_input_file(direction: str) -> Path:
    try:
        from tkinter import Tk, filedialog
    except ImportError as exc:
        raise RuntimeError("tkinter is not available; specify the input path on the command line.") from exc

    filetypes = [("JSON files", "*.json")] if direction == "json2md" else [("Markdown files", "*.md;*.markdown")]
    filetypes.append(("All files", "*.*"))
    root = Tk()
    root.withdraw()
    try:
        selected = filedialog.askopenfilename(title="変換前ファイルを選択", filetypes=filetypes)
    finally:
        root.destroy()
    if not selected:
        raise RuntimeError("Input file selection was cancelled.")
    return Path(selected)


def choose_output_dir() -> Path:
    try:
        from tkinter import Tk, filedialog
    except ImportError as exc:
        raise RuntimeError("tkinter is not available; specify the output path on the command line.") from exc

    root = Tk()
    root.withdraw()
    try:
        selected = filedialog.askdirectory(title="変換後ファイルの保存先フォルダを選択")
    finally:
        root.destroy()
    if not selected:
        raise RuntimeError("Output folder selection was cancelled.")
    return Path(selected)


def default_output_path(input_path: Path, output_dir: Path, direction: str) -> Path:
    suffix = ".md" if direction == "json2md" else ".json"
    return output_dir / input_path.with_suffix(suffix).name


def unused_path(path: Path) -> Path:
    if not path.exists():
        return path
    converted = path.with_name(f"{path.stem}_converted{path.suffix}")
    if not converted.exists():
        return converted
    counter = 2
    while True:
        candidate = path.with_name(f"{path.stem}_converted_{counter}{path.suffix}")
        if not candidate.exists():
            return candidate
        counter += 1


def resolve_paths(args: argparse.Namespace) -> tuple[Path, Path]:
    input_path = args.input
    if input_path is None:
        if not args.gui:
            raise RuntimeError("input is required unless --gui is specified.")
        input_path = choose_input_file(args.direction)

    output_path = args.output
    if output_path is None:
        if args.output_dir is None:
            if not args.gui:
                raise RuntimeError("output or --output-dir is required unless --gui is specified.")
            output_dir = choose_output_dir()
        else:
            output_dir = args.output_dir
        output_path = unused_path(default_output_path(input_path, output_dir, args.direction))
    return input_path, output_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Convert JSON and Markdown using a heading mapping CSV.")
    parser.add_argument("direction", choices=("json2md", "md2json"))
    parser.add_argument("input", nargs="?", type=Path)
    parser.add_argument("output", nargs="?", type=Path)
    parser.add_argument("--mapping", required=True, type=Path)
    parser.add_argument("--gui", action="store_true", help="Show dialogs for the input file and output folder.")
    parser.add_argument("--output-dir", type=Path, help="Output folder used with an automatically generated file name.")
    parser.add_argument("--indent", type=int, default=2)
    args = parser.parse_args()

    try:
        input_path, output_path = resolve_paths(args)
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    entries = load_mapping(args.mapping)
    if args.direction == "json2md":
        data = json.loads(input_path.read_text(encoding="utf-8-sig"))
        output = json_to_markdown(data, entries)
    else:
        data = markdown_to_json(input_path.read_text(encoding="utf-8-sig"), entries)
        output = json.dumps(data, ensure_ascii=False, indent=args.indent) + "\n"

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(output, encoding="utf-8")
    print(output_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
