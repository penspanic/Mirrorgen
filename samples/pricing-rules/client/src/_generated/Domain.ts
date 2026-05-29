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
