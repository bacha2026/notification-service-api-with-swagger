export type Product = {
  id: number;
  name: string;
  shortDescription: string;
  description: string;
  price: number;
  quantityAvailable: number;
  imageUrl: string;
};

export type CartItem = {
  id: number;
  productId: number;
  productName: string;
  unitPrice: number;
  quantity: number;
  subtotal: number;
  imageUrl: string;
};

export type Cart = {
  visitorEmail: string;
  items: CartItem[];
  totalAmount: number;
};

export type OrderItem = {
  productId: number;
  productName: string;
  unitPrice: number;
  quantity: number;
  subtotal: number;
};

export type Order = {
  id: number;
  visitorEmail: string;
  orderStatus: string;
  paymentStatus: string;
  fulfillmentStatus: string;
  deliveryStatus: string;
  totalAmount: number;
  createdAtUtc: string;
  items: OrderItem[];
};
