export interface StoreBasket {
  groceryStoreId: number;
  storeName: string;
  chain?: string;
  total: number;
  coveragePct: number;
  itemsPriced: number;
  itemsTotal: number;
}

export interface ShopRecommendation {
  stores: StoreBasket[];
  best?: StoreBasket;
  explanation: string;
  insufficientData: boolean;
}

export interface BasketItem {
  retailPackagingId: number;
  quantity: number;
}
