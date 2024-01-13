        private UIMenu mainMenu;
        private UIMenu orderMenu;
        private UIMenu ammoMenu;

        private void OpenMainMenu()
        {
            GTA.UI.Notification.PostTicker("main menu open", false, false);
            mainMenu = new UIMenu("Arms Dealer", "Choose an option");

            var placeOrderItem = new UIMenuItem("Place Order", "Place an order for weapons");
            var viewStockItem = new UIMenuItem("View Stock", "View available stock and prices");
            var cancelOrderItem = new UIMenuItem("Cancel Order", "Cancel a pending order");

            mainMenu.AddItem(placeOrderItem);
            mainMenu.AddItem(viewStockItem);
            mainMenu.AddItem(cancelOrderItem);
            mainMenu.RefreshIndex();

            mainMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == placeOrderItem)
                {
                    OpenOrderMenu();
                }
                else if (item == viewStockItem)
                {
                    ViewAvailableStock();
                }
                else if (item == cancelOrderItem)
                {
                    CancelOrder();
                }
            };

            mainMenu.Visible = true;
            iFruit.Close(2000);
        }

        private void OpenOrderMenu()
        {
            orderMenu = new UIMenu("Place Order", "Select items for your order");

            foreach (var item in GetAvailableStock())
            {
                var orderItem = new UIMenuItem(item.Name, $"Price: ${item.Price}");
                orderMenu.AddItem(orderItem);
            }

            var confirmOrderItem = new UIMenuItem("Confirm Order", "Confirm your order");
            var viewOrderItem = new UIMenuItem("View Order", "View current order and total");
            orderMenu.AddItem(confirmOrderItem);
            orderMenu.AddItem(viewOrderItem);

            orderMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == confirmOrderItem)
                {
                    ConfirmOrder();
                }
                else if (item == viewOrderItem)
                {
                    ViewCurrentOrder();
                }
                else
                {
                    // Add or remove the selected item from the order
                    WeaponItem selectedItem = GetAvailableStock()[index];
                    if (currentOrder.Contains(selectedItem))
                    {
                        currentOrder.Remove(selectedItem);
                        GTA.UI.Notification.PostTicker($"{selectedItem.Name} removed from order.", false, false);
                    }
                    else
                    {
                        currentOrder.Add(selectedItem);
                        GTA.UI.Notification.PostTicker($"{selectedItem.Name} added to order.", false, false);
                    }
                }
            };

            orderMenu.Visible = true;
        }

        private void ViewCurrentOrder()
        {
            int total = 0;
            string orderDetails = "Current Order:\n";

            foreach (var item in currentOrder)
            {
                orderDetails += $"{item.Name} - ${item.Price}\n"; // Order Total
                total += item.Price;
            }

            orderDetails += $"Total: ${total}";
            GTA.UI.Notification.PostTicker(orderDetails, false, false);
        }

        private void ConfirmOrder()
        {
            if (currentOrder.Count == 0)
            {
                GTA.UI.Notification.PostTicker("Your order is empty!", false, false);
                return;
            }

            OpenAmmoSelectionMenu(); // Ammo selection menu
        }

        private void OpenAmmoSelectionMenu()
        {
            ammoMenu = new UIMenu("Ammo Selection", "Select ammo for each weapon");

            // Add menu items for each weapon in the order
            foreach (var weapon in currentOrder)
            {
                var ammoItem = new UIMenuSliderItem(weapon.Name + " Ammo", "Select amount of ammo", true)
                {
                    Value = 150 // Default ammo amount
                };
                ammoMenu.AddItem(ammoItem);
            }

            var placeOrderWithAmmoItem = new UIMenuItem("Place Order with Ammo", "Confirm your order with selected ammo");
            var placeOrderWithoutAmmoItem = new UIMenuItem("Place Order without Ammo", "Confirm your order without ammo");
            ammoMenu.AddItem(placeOrderWithAmmoItem);
            ammoMenu.AddItem(placeOrderWithoutAmmoItem);

            ammoMenu.OnItemSelect += (sender, item, index) =>
            {
                if (item == placeOrderWithAmmoItem)
                {
                    // Calculate total cost including ammo
                    int ammoCost = CalculateAmmoCost(ammoMenu);
                    FinalizeOrder(ammoCost);
                }
                else if (item == placeOrderWithoutAmmoItem)
                {
                    FinalizeOrder(0); // No additional cost for ammo
                }
            };

            ammoMenu.Visible = true;
        }
