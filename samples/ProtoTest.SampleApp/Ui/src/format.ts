const date = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric" });
const dateTime = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit"
});
const currency = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const number = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });

export function formatDate(value: string): string {
  return date.format(new Date(value));
}

export function formatDateTime(value: string): string {
  return dateTime.format(new Date(value));
}

export function formatCurrency(value: number): string {
  return currency.format(value);
}

export function formatQuantity(value: number): string {
  return number.format(value);
}

export function shortSha(value: string): string {
  return value.length > 10 ? value.slice(0, 10) : value;
}
