export enum DiscountKind {
  None = 0,
  Flat = 1,
  Percent = 2,
}

export interface OrderLine {
  Quantity: number;
  UnitPrice: number;
  Kind: DiscountKind;
  DiscountValue: number;
}

export function ClampQuantity(requested: number, max: number): number {
  if (requested < 0) {
    return 0;
  }
  if (requested > max) {
    return max;
  }
  return requested;
}

export function Total(unitPrice: number, quantity: number): number {
  return Math.imul(unitPrice, quantity);
}

export function ApplyDiscount(total: number, discountPct: number): number {
  let pct: number = discountPct < 0 ? 0 : (discountPct > 100 ? 100 : discountPct);
  return ((Math.imul(total, (((100 - pct) | 0))) / 100) | 0);
}
