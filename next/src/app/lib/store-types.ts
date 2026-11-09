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
  // The API currently serializes these C# enums as numeric values. Accept
  // strings as well so the UI remains compatible if the API uses enum names.
  orderStatus: string | number;
  paymentStatus: string | number;
  fulfillmentStatus: string | number;
  deliveryStatus: string | number;
  totalAmount: number;
  createdAtUtc: string;
  items: OrderItem[];
};
