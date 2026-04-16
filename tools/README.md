# JSON / Markdown converter

`json_md_converter.py` converts character setting files between JSON and Markdown.
It uses a CSV mapping table whose first column is a JSON path and whose second
column is the Markdown heading to emit or parse.

## Usage

```powershell
python tools\json_md_converter.py json2md input.json output.md --mapping mapping.csv
python tools\json_md_converter.py md2json input.md output.json --mapping mapping.csv
```

To select the input file and output folder with dialogs, use `--gui`.

```powershell
python tools\json_md_converter.py json2md --mapping mapping.csv --gui
python tools\json_md_converter.py md2json --mapping mapping.csv --gui
```

The output file name is generated from the input file name. `json2md` changes
the extension to `.md`, and `md2json` changes it to `.json`. If that file
already exists, `_converted` is added to avoid overwriting it.

The converter preserves the heading order from the mapping CSV. If a child
heading is nested in Markdown but the CSV path omits the immediate parent, the
parent is inferred from the heading level. For example, `features.date` under
`features.birth` is treated as `features.birth.date`.

## Notes

- JSON input must be valid JSON.
- Markdown parsing is intentionally simple and supports the bullet style used by
  the sample character files.
- Known array paths such as `alias`, `role`, `features.likes`, `hobbies`, and
  `ship_life` are restored as arrays even when they contain one item.
- `personality`, `relationship`, and `quotes` are restored as objects from
  `key: value` bullets or two-level bullets.
