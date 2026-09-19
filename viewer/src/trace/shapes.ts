import type { ShapeMismatch, Span } from "./model";
import { shapeMismatches } from "./model";

export type ShapeCheckStatus = "matched" | "failed" | "branch";

export interface ShapeCheckNode {
  key: string;
  path: string;
  status: ShapeCheckStatus;
  mismatch?: ShapeMismatch;
  expected?: unknown;
  actual?: unknown;
  children: ShapeCheckNode[];
}

function shapePathParts(path: string): string[] {
  const normalized = path.trim().replace(/^\$\.?/, "");
  return normalized.match(/[^.[\]]+|\[\d+\]/g) ?? [];
}

function shapeValueAt(value: unknown, path: string): unknown {
  let current = value;
  for (const part of shapePathParts(path)) {
    if (current === null || typeof current !== "object") return undefined;
    const key = part.startsWith("[") ? Number(part.slice(1, -1)) : part;
    current = (current as Record<string | number, unknown>)[key];
  }
  return current;
}

function parse(value: string | null | undefined): unknown {
  if (!value) return undefined;
  try { return JSON.parse(value); } catch { return undefined; }
}

/** Turns matched paths and mismatches into a tree a reader can walk, with the values that were compared. */
export function buildShapeTree(matches: string[], mismatches: ShapeMismatch[], expected?: unknown, actual?: unknown): ShapeCheckNode[] {
  const root: ShapeCheckNode = { key: "$", path: "$", status: "branch", children: [] };
  const add = (path: string, status: "matched" | "failed", mismatch?: ShapeMismatch) => {
    let parent = root;
    let fullPath = "$";
    for (const part of shapePathParts(path)) {
      fullPath += part.startsWith("[") ? part : `.${part}`;
      let child = parent.children.find(item => item.key === part);
      if (!child) {
        child = { key: part, path: fullPath, status: "branch", children: [] };
        parent.children.push(child);
      }
      parent = child;
    }
    parent.status = status;
    parent.mismatch = mismatch;
    parent.expected = mismatch ? mismatch.expected : shapeValueAt(expected, path);
    parent.actual = mismatch ? mismatch.actual : shapeValueAt(actual, path);
  };

  for (const path of matches) add(path, "matched");
  for (const mismatch of mismatches) add(mismatch.path || "$", "failed", mismatch);

  const complete = (node: ShapeCheckNode): ShapeCheckStatus => {
    const childStatuses = node.children.map(complete);
    if (node.status === "failed" || childStatuses.includes("failed")) return node.status = "failed";
    if (node.status === "matched" || (childStatuses.length && childStatuses.every(status => status === "matched"))) return node.status = "matched";
    return node.status = "branch";
  };
  root.children.forEach(complete);
  // A verdict recorded at the path root (JsonShapeMatcher reports "$") belongs to the document
  // itself, so it is the tree; returning only the children would silently drop the mismatch.
  return root.status === "branch" ? root.children : [root];
}

/** A shape check's tree, from the attributes ProtoTest.Json records on the span; null when it recorded none. */
export function shapeTreeOf(span: Span): ShapeCheckNode[] | null {
  const attributes = span.attributes;
  if (!attributes["shape.result"]) return null;
  const matches = parse(attributes["shape.matches"]);
  return buildShapeTree(
    Array.isArray(matches) ? matches.filter((path): path is string => typeof path === "string") : [],
    shapeMismatches(span),
    parse(attributes["shape.expected"]),
    parse(attributes["shape.actual"]));
}
