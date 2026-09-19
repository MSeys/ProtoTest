<script setup lang="ts">
import { ref } from "vue";
import { ApiProblem, downloadArtifact, hasBearerToken, openArtifact } from "../api";
import PageHeader from "../components/PageHeader.vue";

const status = ref<string | null>(null);
const downloading = ref(false);
const xlsxPath = "/api/v1/reports/monthly.xlsx";
const htmlPath = "/api/v1/reports/monthly.html";

async function download(): Promise<void> {
  downloading.value = true;
  status.value = null;
  try {
    await downloadArtifact(xlsxPath, "monthly.xlsx");
    status.value = "monthly.xlsx downloaded.";
  } catch (cause) {
    status.value = cause instanceof ApiProblem ? cause.message : "The report could not be downloaded.";
  } finally {
    downloading.value = false;
  }
}

/**
 * A plain link is enough for the cookie session. A bearer session cannot authenticate a new tab's
 * navigation, so the same document is fetched with the token and opened from a blob URL instead.
 */
async function openHtml(event: MouseEvent): Promise<void> {
  if (!hasBearerToken()) {
    return;
  }

  event.preventDefault();
  status.value = null;
  try {
    await openArtifact(htmlPath);
    status.value = "HTML report opened in a new tab.";
  } catch (cause) {
    status.value = cause instanceof ApiProblem ? cause.message : "The HTML report could not be opened.";
  }
}
</script>

<template>
  <section data-testid="reports-page">
    <PageHeader
      title="Reports"
      description="The monthly project report, for reading or for the spreadsheet on someone else's desk."
      testid="reports-title"
    />

    <div class="reports">
      <section class="panel report">
        <div class="panel__header">
          <h2>Monthly project report</h2>
        </div>
        <div class="panel__body report__body">
          <p class="muted">
            Every project in the organization with its status and environment count, as of the moment the
            report is generated.
          </p>
          <div class="report__actions">
            <button
              class="button button--primary"
              type="button"
              data-testid="report-download"
              :disabled="downloading"
              @click="download"
            >
              Download .xlsx
            </button>
            <a
              class="button"
              :href="htmlPath"
              target="_blank"
              rel="noopener"
              data-testid="report-html-link"
              @click="openHtml"
            >
              View HTML report
            </a>
          </div>
          <p class="report__status" role="status" data-testid="report-status">{{ status ?? "" }}</p>
        </div>
      </section>

      <aside class="reports__note">
        <h3>What each format is for</h3>
        <p class="muted">
          The workbook opens in Excel and other spreadsheet tools. The HTML page is print-friendly and can
          be shared as a link.
        </p>
      </aside>
    </div>
  </section>
</template>

<style scoped>
.reports {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 380px), 1fr));
  gap: 16px;
  align-items: start;
}

.report__body {
  display: grid;
  gap: 14px;
}

.report__actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.report__status {
  min-height: 21px;
  color: var(--ns-muted);
  font-size: 13.5px;
}

.reports__note {
  max-width: 46ch;
}

.reports__note p {
  margin-top: 6px;
}
</style>
