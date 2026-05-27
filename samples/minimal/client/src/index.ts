// Demo entry point — imports the Mirrorgen-emitted module and calls a
// handful of the pricing rules. Run with: npx tsx src/index.ts (after
// you have a runner) or just read it as documentation of the emitted shape.
import { ApplyDiscount, ClampQuantity, Total } from './_generated/rules.ts';

const requested = 12;
const stock = 5;
const unitPrice = 1499;
const discountPct = 15;

const quantity = ClampQuantity(requested, stock);
const subtotal = Total(unitPrice, quantity);
const finalPrice = ApplyDiscount(subtotal, discountPct);

console.log(`requested=${requested}, in-stock=${stock} → quantity=${quantity}`);
console.log(`subtotal=${subtotal}, after ${discountPct}% discount → ${finalPrice}`);
