/**
 * The whole menu: pizzas, then sides, then desserts, each from cheapest to dearest (the Grot Special is the dearest pizza).
 * The `pizzaId` in order lines is the id of any menu item.
 */
export const pizzas = [
  { id: 'marinara', name: 'Marinara', description: 'Tomato, oregano, garlic and olive oil on a crisp base. No cheese, all flavour.', category: 'pizza', price: 8.5, vegetarian: true, popular: false, emoji: '🍅' },
  { id: 'margherita', name: 'Margherita', description: 'San Marzano tomatoes, fresh mozzarella and basil on a blistered crust.', category: 'pizza', price: 9.5, vegetarian: true, popular: false, emoji: '🍕' },
  { id: 'pepperoni', name: 'Pepperoni', description: 'Crisp-edged pepperoni cups over bubbling mozzarella and tomato.', category: 'pizza', price: 11.0, vegetarian: false, popular: true, emoji: '🌶️' },
  { id: 'hawaiian', name: 'Hawaiian', description: 'Smoked ham and caramelised pineapple. Yes, we dare.', category: 'pizza', price: 11.5, vegetarian: false, popular: false, emoji: '🍍' },
  { id: 'veggie-supreme', name: 'Veggie Supreme', description: 'Roasted peppers, mushrooms, red onion, olives and sweetcorn.', category: 'pizza', price: 12.0, vegetarian: true, popular: true, emoji: '🥦' },
  { id: 'diavola', name: 'Diavola', description: 'Spicy salami, fresh chilli, mozzarella and a drizzle of hot honey.', category: 'pizza', price: 12.25, vegetarian: false, popular: false, emoji: '😈' },
  { id: 'bbq-chicken', name: 'BBQ Chicken', description: 'Smoky barbecue sauce, grilled chicken, red onion and fresh coriander.', category: 'pizza', price: 12.5, vegetarian: false, popular: false, emoji: '🍗' },
  { id: 'quattro-formaggi', name: 'Quattro Formaggi', description: 'Mozzarella, gorgonzola, parmesan and fontina, finished with honey.', category: 'pizza', price: 12.5, vegetarian: true, popular: true, emoji: '🧀' },
  { id: 'prosciutto-e-rucola', name: 'Prosciutto e Rucola', description: 'Parma ham, peppery rocket, shaved parmesan and mozzarella on a tomato base.', category: 'pizza', price: 13.0, vegetarian: false, popular: false, emoji: '🥓' },
  { id: 'funghi-tartufo', name: 'Funghi Tartufo', description: 'Wild mushrooms, mozzarella, garlic and thyme, finished with a drizzle of truffle oil.', category: 'pizza', price: 13.5, vegetarian: true, popular: false, emoji: '🍄' },
  { id: 'meat-feast', name: 'Meat Feast', description: 'Spicy sausage, bacon, ham and shredded beef under bubbling mozzarella.', category: 'pizza', price: 13.75, vegetarian: false, popular: false, emoji: '🍖' },
  { id: 'grot-special', name: 'Grot Special', description: 'The house favourite: spicy sausage, roasted garlic, charred orange peel and a drizzle of chilli oil.', category: 'pizza', price: 14.0, vegetarian: false, popular: true, emoji: '🔥' },
  { id: 'coleslaw', name: 'Coleslaw', description: 'Crunchy cabbage and carrot in a creamy, tangy dressing.', category: 'side', price: 3.0, vegetarian: true, popular: false, emoji: '🥕' },
  { id: 'marinated-olives', name: 'Marinated Olives', description: 'Warm green and black olives with orange zest, rosemary and chilli.', category: 'side', price: 3.5, vegetarian: true, popular: true, emoji: '🫒' },
  { id: 'potato-wedges', name: 'Potato Wedges', description: 'Skin-on wedges, crisp outside and fluffy inside, with smoked paprika and a sour cream dip.', category: 'side', price: 4.0, vegetarian: true, popular: false, emoji: '🥔' },
  { id: 'side-salad', name: 'Side Salad', description: 'Crisp leaves, cherry tomatoes, cucumber and red onion with a lemon and olive oil dressing.', category: 'side', price: 4.0, vegetarian: true, popular: true, emoji: '🥗' },
  { id: 'onion-rings', name: 'Onion Rings', description: 'Thick-cut onions in a crunchy beer-free batter, served with a smoky barbecue dip.', category: 'side', price: 4.25, vegetarian: true, popular: false, emoji: '🧅' },
  { id: 'garlic-bread', name: 'Garlic Bread', description: 'Wood-fired ciabatta with roasted garlic butter, parsley and sea salt. A taste sensation.', category: 'side', price: 4.5, vegetarian: true, popular: true, emoji: '🥖' },
  { id: 'sweet-potato-fries', name: 'Sweet Potato Fries', description: 'Crisp-edged fries with a pinch of sea salt and a chipotle mayo dip.', category: 'side', price: 4.5, vegetarian: true, popular: true, emoji: '🍠' },
  { id: 'dough-balls', name: 'Dough Balls', description: 'Warm, pillowy dough balls tossed in herb butter, with a garlic dip.', category: 'side', price: 4.75, vegetarian: true, popular: false, emoji: '🥯' },
  { id: 'garlic-cheese-flatbread', name: 'Garlic & Cheese Flatbread', description: 'Stone-baked flatbread with melted mozzarella, roasted garlic and oregano.', category: 'side', price: 5.25, vegetarian: true, popular: false, emoji: '🫓' },
  { id: 'caesar-salad', name: 'Caesar Salad', description: 'Cos lettuce, parmesan, croutons and a classic Caesar dressing.', category: 'side', price: 5.5, vegetarian: false, popular: false, emoji: '🥬' },
  { id: 'mozzarella-sticks', name: 'Mozzarella Sticks', description: 'Golden, crumb-coated mozzarella with a sweet chilli dip.', category: 'side', price: 5.75, vegetarian: true, popular: false, emoji: '🍢' },
  { id: 'chicken-wings', name: 'Chicken Wings', description: 'Twelve crispy wings glazed in smoky barbecue sauce.', category: 'side', price: 6.5, vegetarian: false, popular: true, emoji: '🍗' },
  { id: 'lemon-sorbet', name: 'Lemon Sorbet', description: 'Zesty, dairy-free sorbet to cleanse the palate after a fiery pizza.', category: 'dessert', price: 3.75, vegetarian: true, popular: false, emoji: '🍋' },
  { id: 'vanilla-gelato', name: 'Vanilla Gelato', description: 'Slow-churned Madagascan vanilla gelato, served in a chilled bowl.', category: 'dessert', price: 4.0, vegetarian: true, popular: true, emoji: '🍨' },
  { id: 'chocolate-brownie', name: 'Chocolate Brownie', description: 'Warm, fudgy brownie with a molten centre, served with a scoop of vanilla gelato.', category: 'dessert', price: 4.75, vegetarian: true, popular: false, emoji: '🍫' },
  { id: 'affogato', name: 'Affogato', description: 'A scoop of vanilla gelato drowned in a shot of hot espresso.', category: 'dessert', price: 5.0, vegetarian: true, popular: false, emoji: '☕' },
  { id: 'strawberry-cheesecake', name: 'Strawberry Cheesecake', description: 'Creamy baked cheesecake on a biscuit base, with macerated strawberries.', category: 'dessert', price: 5.5, vegetarian: true, popular: false, emoji: '🍓' },
  { id: 'tiramisu', name: 'Tiramisu', description: 'Espresso-soaked sponge, mascarpone cream and a dusting of cocoa.', category: 'dessert', price: 5.75, vegetarian: true, popular: true, emoji: '🍰' },
];

const byId = new Map(pizzas.map((p) => [p.id, p]));

export const findPizza = (id) => byId.get(id);

export const round2 = (value) => Math.round((value + Number.EPSILON) * 100) / 100;
