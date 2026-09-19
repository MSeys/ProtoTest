<script setup lang="ts">
import { onMounted, ref } from "vue";
import { ApiProblem, getSubscription, listInvoices, payInvoice, type Invoice, type Subscription } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatCurrency, formatDate, formatQuantity } from "../format";
import { toast } from "../toasts";

const PAYMENT_METHODS = [
  { value: "pm_card_visa", label: "Visa •••• 4242" },
  { value: "pm_card_mastercard", label: "Mastercard •••• 4444" },
  { value: "pm_card_declined", label: "Declined card (test)" }
];

const subscription = ref<Subscription | null>(null);
const invoices = ref<Invoice[]>([]);
const loading = ref(true);
const error = ref<string | null>(null);
const methods = ref<Record<number, string>>({});
const paying = ref<number | null>(null);

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    const [restSubscription, restInvoices] = await Promise.all([getSubscription(), listInvoices()]);
    subscription.value = restSubscription;
    invoices.value = restInvoices.items;
    for (const invoice of restInvoices.items) {
      methods.value[invoice.id] ??= PAYMENT_METHODS[0].value;
    }
  } catch (cause) {
    error.value = cause instanceof ApiProblem ? cause.message : "Billing could not be loaded.";
  } finally {
    loading.value = false;
  }
}

async function pay(invoice: Invoice): Promise<void> {
  paying.value = invoice.id;
  try {
    const updated = await payInvoice(invoice.id, methods.value[invoice.id] ?? PAYMENT_METHODS[0].value);
    invoices.value = invoices.value.map((item) => (item.id === updated.id ? updated : item));
    if (updated.status === "paid") {
      toast("success", `${updated.number} paid.`);
    } else {
      const reason = updated.payments.at(-1)?.failureReason ?? "the payment failed";
      toast("error", `${updated.number} was not paid: ${reason.replace(/_/g, " ")}.`);
    }
  } catch (cause) {
    toast("error", cause instanceof ApiProblem ? cause.message : "The payment could not be processed.");
  } finally {
    paying.value = null;
  }
}

function isPayable(invoice: Invoice): boolean {
  return invoice.status !== "paid" && invoice.status !== "void";
}

onMounted(load);
</script>

<template>
  <section data-testid="billing-page">
    <PageHeader
      title="Billing"
      description="Invoices, payments and the plan this organization is on."
      testid="billing-title"
    >
      <template #actions>
        <button class="button" type="button" data-testid="billing-refresh" :disabled="loading" @click="load">
          Refresh
        </button>
      </template>
    </PageHeader>

    <p v-if="error" class="notice billing-error" data-tone="danger" data-testid="billing-error">
      {{ error }}
    </p>

    <section class="panel subscription">
      <div class="panel__header">
        <h2>Subscription</h2>
        <StatusBadge v-if="subscription" :status="subscription.status" testid="subscription-status" />
      </div>
      <LoadingBlock v-if="loading" :rows="2" label="Loading subscription" />
      <div v-else-if="subscription" class="panel__body subscription__body" data-testid="subscription">
        <div class="subscription__facts">
          <div>
            <p class="subscription__label">Plan</p>
            <p class="subscription__value" data-testid="subscription-plan">{{ subscription.planName }}</p>
          </div>
          <div>
            <p class="subscription__label">Seats</p>
            <p class="subscription__value" data-testid="subscription-seats">
              {{ subscription.seats }} <span class="muted">of {{ subscription.includedSeats }} included</span>
            </p>
          </div>
          <div>
            <p class="subscription__label">Deploy minutes included</p>
            <p class="subscription__value" data-testid="subscription-deploy-minutes">
              {{ formatQuantity(subscription.includedDeployMinutes) }} min
            </p>
          </div>
          <div>
            <p class="subscription__label">Renews</p>
            <p class="subscription__value" data-testid="subscription-renewal">
              {{ formatDate(subscription.currentPeriodEndUtc) }}
            </p>
          </div>
        </div>
        <p v-if="subscription.cancelAtPeriodEnd" class="notice" data-tone="warning" data-testid="subscription-canceling">
          This subscription is scheduled to end at the close of the current period.
        </p>
      </div>
    </section>

    <section class="panel invoices">
      <div class="panel__header">
        <h2>Invoices</h2>
      </div>

      <LoadingBlock v-if="loading" :rows="4" label="Loading invoices" />

      <EmptyState
        v-else-if="invoices.length === 0"
        title="No invoices yet"
        message="An invoice is issued when a billing period closes. Nothing is due right now."
        testid="invoices-empty"
      />

      <div v-else class="table-scroll">
        <table class="table" data-testid="invoices-table">
          <thead>
            <tr>
              <th scope="col">Invoice</th>
              <th scope="col">Status</th>
              <th scope="col">Issued</th>
              <th scope="col">Due</th>
              <th scope="col" class="amount">Total</th>
              <th scope="col">Payment</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="invoice in invoices"
              :key="invoice.id"
              data-testid="invoice-row"
              :data-invoice-number="invoice.number"
              :data-invoice-status="invoice.status"
            >
              <td>
                <span class="mono" data-testid="invoice-number">{{ invoice.number }}</span>
              </td>
              <td><StatusBadge :status="invoice.status" testid="invoice-status" /></td>
              <td class="muted" data-testid="invoice-issued">{{ formatDate(invoice.issuedAtUtc) }}</td>
              <td class="muted" data-testid="invoice-due">{{ formatDate(invoice.dueAtUtc) }}</td>
              <td class="amount" data-testid="invoice-total">{{ formatCurrency(invoice.total) }}</td>
              <td>
                <div class="pay">
                  <label class="visually-hidden" :for="`pay-method-${invoice.id}`">
                    Payment method for {{ invoice.number }}
                  </label>
                  <select
                    :id="`pay-method-${invoice.id}`"
                    v-model="methods[invoice.id]"
                    class="select pay__method"
                    data-testid="pay-method"
                    :disabled="!isPayable(invoice)"
                  >
                    <option v-for="method in PAYMENT_METHODS" :key="method.value" :value="method.value">
                      {{ method.label }}
                    </option>
                  </select>
                  <button
                    class="button button--small"
                    type="button"
                    data-testid="pay"
                    :disabled="!isPayable(invoice) || paying === invoice.id"
                    @click="pay(invoice)"
                  >
                    Pay
                  </button>
                  <StatusBadge
                    v-if="invoice.payments.length"
                    :status="invoice.payments.at(-1)!.status"
                    testid="invoice-last-payment"
                  />
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="panel__body muted invoices__note">
        Paying an invoice publishes the <span class="mono">invoice.paid</span> event and notifies webhook
        endpoints subscribed to it.
      </p>
    </section>
  </section>
</template>

<style scoped>
.billing-error {
  margin-bottom: 16px;
}

.subscription {
  margin-bottom: 18px;
}

.subscription__body {
  display: grid;
  gap: 14px;
}

.subscription__facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 170px), 1fr));
  gap: 14px;
}

.subscription__label {
  color: var(--ns-muted);
  font-size: 12.5px;
  font-weight: 600;
}

.subscription__value {
  margin-top: 3px;
  font-size: 15px;
  font-weight: 600;
}

.amount {
  text-align: right;
}

.pay {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  justify-content: flex-end;
}

.pay__method {
  width: auto;
  min-width: 170px;
}

.invoices__note {
  border-top: 1px solid var(--ns-line-soft);
  font-size: 13px;
}
</style>
