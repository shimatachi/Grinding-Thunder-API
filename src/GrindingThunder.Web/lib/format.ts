export function formatNumber(value: number): string {
  return new Intl.NumberFormat("en-US").format(value);
}

export function formatOptimizationMode(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}
