import { describe, expect, it } from "vitest";
import { storedZip } from "../testing/storedZip";
import { columnName, readWorkbook } from "./workbook";

describe("readWorkbook", () => {
  it("reads shared, boolean, date and inline values", async () => {
    const preview = await readWorkbook(new Blob([workbookZip(sheet(`
      <row r="1">
        <c r="A1" t="s"><v>0</v></c>
        <c r="B1" t="b"><v>1</v></c>
        <c r="C1" s="1"><v>45292</v></c>
      </row>
      <row r="2"><c r="A2" t="inlineStr"><is><t>inline</t></is></c></row>`))]));

    expect(preview.sheets).toHaveLength(1);
    expect(preview.sheets[0]).toMatchObject({
      name: "Summary",
      hidden: false,
      rowCount: 2,
      columnCount: 3,
      truncated: false,
      rows: [["Atlas", "TRUE", "2024-01-01"], ["inline", "", ""]]
    });
  });

  it("normalizes relationship paths and preserves hidden state", async () => {
    const preview = await readWorkbook(new Blob([workbookZip(
      sheet(`<row r="1"><c r="A1"><v>42</v></c></row>`),
      ".\\worksheets\\sheet1.xml",
      "hidden")]));

    expect(preview.sheets[0]).toMatchObject({ name: "Summary", hidden: true, rows: [["42"]] });
  });

  it("marks a preview as truncated without allocating rows beyond the limit", async () => {
    const preview = await readWorkbook(new Blob([workbookZip(
      sheet(`<row r="501"><c r="A501"><v>last</v></c></row>`))]));

    expect(preview.sheets[0].truncated).toBe(true);
    expect(preview.sheets[0].rowCount).toBe(501);
    expect(preview.sheets[0].rows).toHaveLength(500);
  });

  it("reports invalid containers and XML as workbook errors", async () => {
    await expect(readWorkbook(new Blob([new Uint8Array([1, 2, 3])]))).rejects.toThrow("not a readable .xlsx");
    await expect(readWorkbook(new Blob([storedZip({
      "xl/workbook.xml": "<workbook>",
      "xl/_rels/workbook.xml.rels": relationships("worksheets/sheet1.xml"),
      "xl/worksheets/sheet1.xml": sheet("")
    })]))).rejects.toThrow("workbook.xml is not valid XML");
  });

  it("reports a damaged directory offset without leaking a DataView error", async () => {
    const bytes = new Uint8Array(workbookZip(sheet("")));
    const eocd = bytes.length - 22;
    new DataView(bytes.buffer).setUint32(eocd + 16, bytes.length - 2, true);

    await expect(readWorkbook(new Blob([bytes]))).rejects.toThrow("ZIP directory is damaged");
  });

  it("names spreadsheet columns", () => {
    expect([0, 25, 26, 27, 701].map(columnName)).toEqual(["A", "Z", "AA", "AB", "ZZ"]);
  });
});

function workbookZip(worksheet: string, target = "worksheets/sheet1.xml", state = "visible"): ArrayBuffer {
  return storedZip({
    "xl/workbook.xml": `<?xml version="1.0"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Summary" state="${state}" sheetId="1" r:id="rId1"/></sheets></workbook>`,
    "xl/_rels/workbook.xml.rels": relationships(target),
    "xl/worksheets/sheet1.xml": worksheet,
    "xl/sharedStrings.xml": `<?xml version="1.0"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Atlas</t></si></sst>`,
    "xl/styles.xml": `<?xml version="1.0"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="14"/></cellXfs></styleSheet>`
  });
}

function relationships(target: string): string {
  return `<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="${target}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"/></Relationships>`;
}

function sheet(rows: string): string {
  return `<?xml version="1.0"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>${rows}</sheetData></worksheet>`;
}
