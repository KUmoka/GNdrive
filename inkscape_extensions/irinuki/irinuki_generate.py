#!/usr/bin/env python3
"""Generate an editable symmetrical iri-nuki path from a curve and a 2-node path."""

from __future__ import annotations

import copy
import math
from typing import List, Sequence, Tuple

import inkex


Point = Tuple[float, float]
Cubic = Tuple[Point, Point, Point, Point]

EPSILON = 1.0e-9
INKSCAPE_ORIGINAL_D = inkex.addNS("original-d", "inkscape")
INKSCAPE_PATH_EFFECT = inkex.addNS("path-effect", "inkscape")
SODIPODI_NODETYPES = inkex.addNS("nodetypes", "sodipodi")


class IrinukiError(ValueError):
    """A user-facing geometry or input validation error."""


def as_point(value) -> Point:
    return (float(value[0]), float(value[1]))


def add(a: Point, b: Point) -> Point:
    return (a[0] + b[0], a[1] + b[1])


def sub(a: Point, b: Point) -> Point:
    return (a[0] - b[0], a[1] - b[1])


def mul(a: Point, scale: float) -> Point:
    return (a[0] * scale, a[1] * scale)


def dot(a: Point, b: Point) -> float:
    return a[0] * b[0] + a[1] * b[1]


def length(a: Point) -> float:
    return math.hypot(a[0], a[1])


def distance(a: Point, b: Point) -> float:
    return length(sub(a, b))


def lerp(a: Point, b: Point, t: float) -> Point:
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)


def normalize(a: Point) -> Point:
    current = length(a)
    if current <= EPSILON:
        raise IrinukiError("接線ベクトルがゼロです。対象区間の形状を確認してください。")
    return (a[0] / current, a[1] / current)


def cubic_point(cubic: Cubic, t: float) -> Point:
    p0, p1, p2, p3 = cubic
    q0 = lerp(p0, p1, t)
    q1 = lerp(p1, p2, t)
    q2 = lerp(p2, p3, t)
    r0 = lerp(q0, q1, t)
    r1 = lerp(q1, q2, t)
    return lerp(r0, r1, t)


def split_cubic(cubic: Cubic, t: float) -> Tuple[Cubic, Cubic]:
    p0, p1, p2, p3 = cubic
    q0 = lerp(p0, p1, t)
    q1 = lerp(p1, p2, t)
    q2 = lerp(p2, p3, t)
    r0 = lerp(q0, q1, t)
    r1 = lerp(q1, q2, t)
    s = lerp(r0, r1, t)
    return (p0, q0, r0, s), (s, r1, q2, p3)


def reverse_cubic(cubic: Cubic) -> Cubic:
    p0, p1, p2, p3 = cubic
    return (p3, p2, p1, p0)


def reflected_point(point: Point, origin: Point, unit_axis: Point) -> Point:
    v = sub(point, origin)
    projected = mul(unit_axis, 2.0 * dot(v, unit_axis))
    return add(origin, sub(projected, v))


def reflected_cubic(cubic: Cubic, origin: Point, unit_axis: Point) -> Cubic:
    return tuple(reflected_point(point, origin, unit_axis) for point in cubic)  # type: ignore[return-value]


def sample_cubic(cubic: Cubic, samples: int) -> List[Point]:
    samples = max(2, int(samples))
    return [cubic_point(cubic, i / samples) for i in range(samples + 1)]


def cumulative_lengths(points: Sequence[Point]) -> List[float]:
    totals = [0.0]
    for previous, current in zip(points, points[1:]):
        totals.append(totals[-1] + distance(previous, current))
    return totals


def parameter_at_length(cubic: Cubic, target_length: float, samples: int) -> Tuple[float, float]:
    points = sample_cubic(cubic, samples)
    totals = cumulative_lengths(points)
    total_length = totals[-1]
    if target_length <= 0.0:
        return 0.0, total_length
    if target_length >= total_length:
        return 1.0, total_length

    for index in range(1, len(totals)):
        if totals[index] >= target_length:
            previous = totals[index - 1]
            segment_length = totals[index] - previous
            local = 0.0 if segment_length <= EPSILON else (target_length - previous) / segment_length
            return ((index - 1) + local) / samples, total_length

    return 1.0, total_length


def distance_to_cubic(point: Point, cubic: Cubic, samples: int) -> float:
    return min(distance(point, sample) for sample in sample_cubic(cubic, samples))


def collect_cubics(csp) -> List[Cubic]:
    cubics: List[Cubic] = []
    for subpath in csp:
        limit = effective_node_count(subpath)
        if limit < 2:
            continue
        for index in range(1, limit):
            cubics.append(cubic_between(subpath[index - 1], subpath[index]))
        if is_closed_subpath(subpath):
            cubics.append(cubic_between(subpath[limit - 1], subpath[limit]))
    return cubics


def distance_to_cubics(point: Point, cubics: Sequence[Cubic], samples: int) -> float:
    if not cubics:
        raise IrinukiError("Aの曲線区間が見つかりません。")
    return min(distance_to_cubic(point, cubic, samples) for cubic in cubics)


def fmt(value: float) -> str:
    text = f"{value:.6f}".rstrip("0").rstrip(".")
    return text if text and text != "-0" else "0"


def cubic_path_d(first: Cubic, second: Cubic) -> str:
    p0, p1, p2, p3 = first
    _, q1, q2, q3 = second
    return (
        f"M {fmt(p0[0])} {fmt(p0[1])} "
        f"C {fmt(p1[0])} {fmt(p1[1])} {fmt(p2[0])} {fmt(p2[1])} {fmt(p3[0])} {fmt(p3[1])} "
        f"C {fmt(q1[0])} {fmt(q1[1])} {fmt(q2[0])} {fmt(q2[1])} {fmt(q3[0])} {fmt(q3[1])}"
    )


def get_path_data(element: inkex.PathElement, prefer_original: bool = False) -> str:
    original = element.get(INKSCAPE_ORIGINAL_D)
    current = element.get("d")
    data = original if prefer_original and original else current
    if not data:
        raise IrinukiError(f"{element.get_id()} にパスデータがありません。")
    return data


def composed_transform(element) -> inkex.Transform:
    try:
        return element.composed_transform()
    except AttributeError:
        return inkex.Transform()


def transform_point(transform: inkex.Transform, point: Point) -> Point:
    transformed = transform.apply_to_point(point)
    return (float(transformed.x), float(transformed.y))


def transformed_cubic_superpath(element: inkex.PathElement, prefer_original: bool = False):
    path = inkex.Path(get_path_data(element, prefer_original=prefer_original))
    csp = path.to_superpath()
    transform = composed_transform(element)
    for subpath in csp:
        for node in subpath:
            for index, point in enumerate(node):
                node[index] = list(transform_point(transform, as_point(point)))
    return csp


def is_closed_subpath(subpath) -> bool:
    return len(subpath) > 1 and distance(as_point(subpath[0][1]), as_point(subpath[-1][1])) <= 1.0e-7


def effective_node_count(subpath) -> int:
    if is_closed_subpath(subpath):
        return len(subpath) - 1
    return len(subpath)


def collect_nodes(csp) -> List[dict]:
    nodes = []
    for subpath_index, subpath in enumerate(csp):
        for node_index in range(effective_node_count(subpath)):
            nodes.append(
                {
                    "subpath": subpath_index,
                    "index": node_index,
                    "point": as_point(subpath[node_index][1]),
                }
            )
    return nodes


def total_node_count(element: inkex.PathElement, prefer_original: bool = False) -> int:
    return len(collect_nodes(transformed_cubic_superpath(element, prefer_original=prefer_original)))


def cubic_between(start_node, end_node) -> Cubic:
    return (
        as_point(start_node[1]),
        as_point(start_node[2]),
        as_point(end_node[0]),
        as_point(end_node[1]),
    )


def adjacent_cubics_ending_at_node(csp, node_ref: dict) -> List[Cubic]:
    subpath = csp[node_ref["subpath"]]
    index = node_ref["index"]
    limit = effective_node_count(subpath)
    closed = is_closed_subpath(subpath)
    candidates: List[Cubic] = []

    if index > 0:
        candidates.append(cubic_between(subpath[index - 1], subpath[index]))
    elif closed:
        candidates.append(cubic_between(subpath[limit - 1], subpath[limit]))

    if index < limit - 1:
        candidates.append(reverse_cubic(cubic_between(subpath[index], subpath[index + 1])))
    elif closed:
        candidates.append(reverse_cubic(cubic_between(subpath[index], subpath[limit])))

    return candidates


def b_endpoints(element: inkex.PathElement) -> Tuple[Point, Point]:
    csp = transformed_cubic_superpath(element, prefer_original=True)
    nodes = collect_nodes(csp)
    if len(nodes) != 2:
        raise IrinukiError("Bは2ノードのみの通常パスにしてください。")
    return nodes[0]["point"], nodes[1]["point"]


def choose_a_and_b(paths: Sequence[inkex.PathElement]) -> Tuple[inkex.PathElement, inkex.PathElement]:
    first, second = paths
    first_count = total_node_count(first, prefer_original=True)
    second_count = total_node_count(second, prefer_original=True)

    if second_count == 2 and first_count >= 2:
        return first, second
    if first_count == 2 and second_count >= 2 and second_count != 2:
        return second, first
    if second_count != 2:
        raise IrinukiError("後に選んだパスBは2ノードのみの通常パスにしてください。")
    return first, second


def target_geometry(
    a_element: inkex.PathElement,
    b_element: inkex.PathElement,
    samples: int,
    error_when_too_long: bool,
) -> Tuple[Cubic, Cubic]:
    a_csp = transformed_cubic_superpath(a_element, prefer_original=False)
    a_nodes = collect_nodes(a_csp)
    if len(a_nodes) < 2:
        raise IrinukiError("Aは最低2ノード以上の通常パスにしてください。")
    a_cubics = collect_cubics(a_csp)

    b0, b1 = b_endpoints(b_element)
    lb = distance(b0, b1)
    if lb <= EPSILON:
        raise IrinukiError("Bの長さがゼロです。")

    nb1 = min((b0, b1), key=lambda point: distance_to_cubics(point, a_cubics, min(samples, 80)))
    nb2 = b1 if nb1 == b0 else b0
    nb_midpoint = mul(add(nb1, nb2), 0.5)
    nan = min(a_nodes, key=lambda node: distance(node["point"], nb_midpoint))
    candidates = adjacent_cubics_ending_at_node(a_csp, nan)
    if not candidates:
        raise IrinukiError("NAnに隣接するAの区間が見つかりません。")

    target_cubic = min(candidates, key=lambda cubic: distance_to_cubic(nb1, cubic, min(samples, 80)))
    points = sample_cubic(target_cubic, samples)
    totals = cumulative_lengths(points)
    la = totals[-1]
    half_b = lb / 2.0
    if half_b > la + 1.0e-7:
        if error_when_too_long:
            raise IrinukiError("B is too long for the target segment.")
        target_length = 0.0
    else:
        target_length = max(0.0, la - half_b)

    t_target, _ = parameter_at_length(target_cubic, target_length, samples)
    _, sub_curve = split_cubic(target_cubic, t_target)

    tangent = sub(target_cubic[3], target_cubic[2])
    if length(tangent) <= EPSILON:
        tangent = sub(cubic_point(target_cubic, 1.0), cubic_point(target_cubic, max(0.0, 1.0 - 1.0 / samples)))
    unit_tangent = normalize(tangent)
    unit_normal = (-unit_tangent[1], unit_tangent[0])
    nan_point = target_cubic[3]

    mirrored_from_nb2_to_nan = reflected_cubic(sub_curve, nan_point, unit_normal)
    mirrored_from_nan_to_nb2 = reverse_cubic(mirrored_from_nb2_to_nan)
    return sub_curve, mirrored_from_nan_to_nb2


def path_d_in_parent_space(element: inkex.PathElement, first: Cubic, second: Cubic) -> str:
    parent = element.getparent()
    parent_transform = composed_transform(parent) if parent is not None else inkex.Transform()
    inverse_parent = -parent_transform

    def local_cubic(cubic: Cubic) -> Cubic:
        return tuple(transform_point(inverse_parent, point) for point in cubic)  # type: ignore[return-value]

    return cubic_path_d(local_cubic(first), local_cubic(second))


def set_generated_path(
    element: inkex.PathElement,
    path_d: str,
    preserve_effects: bool,
) -> None:
    element.set("d", path_d)
    element.attrib.pop("transform", None)
    element.attrib.pop(SODIPODI_NODETYPES, None)

    has_lpe = element.get(INKSCAPE_PATH_EFFECT) is not None
    if preserve_effects and has_lpe:
        element.set(INKSCAPE_ORIGINAL_D, path_d)
    elif not preserve_effects:
        element.attrib.pop(INKSCAPE_ORIGINAL_D, None)
        element.attrib.pop(INKSCAPE_PATH_EFFECT, None)


def append_after(reference, new_element) -> None:
    parent = reference.getparent()
    if parent is None:
        raise IrinukiError("Bの親要素が見つかりません。")
    parent.insert(parent.index(reference) + 1, new_element)


def hide_element(element) -> None:
    try:
        element.style["display"] = "none"
    except AttributeError:
        style = element.get("style", "")
        separator = "" if not style or style.endswith(";") else ";"
        element.set("style", f"{style}{separator}display:none")


class IrinukiGenerate(inkex.EffectExtension):
    def add_arguments(self, pars):
        pars.add_argument("--input_mode", default="selection")
        pars.add_argument("--output_mode", default="replace")
        pars.add_argument("--preserve_effects", type=inkex.Boolean, default=True)
        pars.add_argument("--error_when_too_long", type=inkex.Boolean, default=True)
        pars.add_argument("--samples", type=int, default=200)

    def selected_paths(self) -> List[inkex.PathElement]:
        if self.options.input_mode == "stack":
            selected = list(self.svg.selection.rendering_order())
        else:
            selected = list(self.svg.selection.values())
        return [element for element in selected if isinstance(element, inkex.PathElement)]

    def effect(self):
        paths = self.selected_paths()
        if len(paths) != 2:
            raise IrinukiError("曲線Aと2ノード線分Bの2つのパスだけを選択してください。")

        a_element, b_element = choose_a_and_b(paths)
        first, second = target_geometry(
            a_element,
            b_element,
            max(20, int(self.options.samples)),
            bool(self.options.error_when_too_long),
        )
        path_d = path_d_in_parent_space(b_element, first, second)

        if self.options.output_mode == "replace":
            set_generated_path(b_element, path_d, bool(self.options.preserve_effects))
            return

        new_element = copy.deepcopy(b_element)
        new_element.set("id", self.svg.get_unique_id("irinuki"))
        set_generated_path(new_element, path_d, bool(self.options.preserve_effects))
        append_after(b_element, new_element)

        if self.options.output_mode == "hide":
            hide_element(b_element)


if __name__ == "__main__":
    try:
        IrinukiGenerate().run()
    except IrinukiError as error:
        inkex.errormsg(str(error))
        raise SystemExit(1)
