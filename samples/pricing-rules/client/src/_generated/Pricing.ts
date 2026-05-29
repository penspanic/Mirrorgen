export function TierDiscountBps(tier: CustomerTier): number {
  return ((): number => { const _v = tier; if (_v === CustomerTier.Bronze) return 0; if (_v === CustomerTier.Silver) return 250; if (_v === CustomerTier.Gold) return 750; if (_v === CustomerTier.Platinum) return 1500; return 0; })();
}

export function ShippingFeeCents(distanceKm: number): number {
  return ((): number => { const _v = distanceKm; if (_v < 0) return 0; if (_v < 50) return 300; if (_v < 200) return 800; if (_v < 1000) return 1800; return 4500; })();
}

export function ApplyTaxCents(amountCents: number, taxBps: number): number {
  let clampedBps: number = ((): number => { const _v = taxBps; if (_v < 0) return 0; if (_v > 10000) return 10000; return taxBps; })();
  return ((amountCents + (((Math.imul(amountCents, clampedBps) / 10000) | 0))) | 0);
}

export function FinalPriceCents(subtotalCents: number, tier: CustomerTier, taxBps: number, distanceKm: number): number {
  let discountBps: number = TierDiscountBps(tier);
  let afterDiscount: number = ((subtotalCents - (((Math.imul(subtotalCents, discountBps) / 10000) | 0))) | 0);
  let afterTax: number = ApplyTaxCents(afterDiscount, taxBps);
  return ((afterTax + ShippingFeeCents(distanceKm)) | 0);
}

export function HalvingRounds(value: number): number {
  let v: number = ((): number => { const _v = value; if (_v < 0) return -value; return value; })();
  if (v > 1000000) {
    v = 1000000;
  }
  let rounds: number = 0;
  while (v > 0) {
    v = ((v / 2) | 0);
    rounds++;
    if (rounds > 64) {
      break;
    }
  }
  return rounds;
}

export function LineSubtotalCents(line: OrderLine): number {
  let qty: number = ((): number => { const _v = line.Quantity; if (_v < 0) return 0; if (_v > 999) return 999; return line.Quantity; })();
  return Math.imul(line.UnitPrice.Cents, qty);
}

export function ZoneForDistance(distanceKm: number): ShippingZone {
  return ((): ShippingZone => { const _v = distanceKm; if ((_v >= 0 && _v < 50)) return ShippingZone.Local; if ((_v >= 50 && _v < 1000)) return ShippingZone.Regional; return ShippingZone.International; })();
}

export function CartSubtotalCents(lines: OrderLine[]): number {
  let total: number = 0;
  for (const l of lines) {
    total = ((total + LineSubtotalCents(l)) | 0);
  }
  return total;
}

export interface Money {
  Cents: number;
}

export enum CustomerTier {
  Bronze = 0,
  Silver = 1,
  Gold = 2,
  Platinum = 3,
}

export enum ShippingZone {
  Local = 0,
  Regional = 1,
  International = 2,
}

export interface OrderLine {
  Product: number;
  Quantity: number;
  UnitPrice: Money;
}
