using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using iFruitAddon2;
using NativeUI;

namespace ArmsDealer // v1.0.0 by Frosty
{
    public class Main : Script
    {
        private CustomiFruit iFruit;
        private iFruitContact armsDealerContact;
        private UIMenu mainMenu;
        private UIMenu orderMenu;
        private GameTimer orderTimer;
        private Random random = new Random();
        private List<WeaponItem> currentOrder = new List<WeaponItem>();
        private List<WeaponItem> availableStock = new List<WeaponItem>();
        private TimeSpan lastStockUpdateTime;
        private Vehicle deliveryVehicle;
        private Blip deliveryBlip;
        private DateTime vehicleSpawnTime;
        private bool isOrderStolen = false;

        private List<Location> deliveryLocations = new List<Location>
        {
            // LS Locations
            new Location(new Vector3(-13.72f, -1817.01f, 25.53f), 319.48f), // Grove St
            new Location(new Vector3(358.85f, -2441.50f, 6.10f), 0.37f), // LS Port Area
            new Location(new Vector3(1701.80f, -1432.06f, 112.52f), 291.39f), // LS Oil Field
            new Location(new Vector3(-1613.00f, -1000.21f, 7.35f), 228.21f), // Under Pier
            new Location(new Vector3(1448.23f, 1128.80f, 114.03f), 269.22f), // Madrazo House
            new Location(new Vector3(35.68f, -1021.58f, 29.17f), 339.98f), // Near Center LS
            // North San Andreas Locations
            new Location(new Vector3(2682.35f, 3293.92f, 54.94f), 62.72f), // Highway Gas Station
            new Location(new Vector3(1486.95f, 3752.86f, 33.50f), 31.20f), // Boat Motel
            new Location(new Vector3(2297.00f, 4889.65f, 41.09f), 132.72f), // Meth Farm
            new Location(new Vector3(1689.25f, 6435.63f, 32.25f), 333.32f), // North SA Gas Sation
        };

        private List<WeaponItem> allPossibleWeapons = new List<WeaponItem>
        {
            new WeaponItem("Pistol", 10),
            new WeaponItem("Assault Rifle", 20),
            new WeaponItem("Sniper Rifle", 1500),
            new WeaponItem("Shotgun", 750),
            new WeaponItem("SMG", 600),
            // Add more weapons here...
        };

        public Main()
        {
            iFruit = new CustomiFruit();
            armsDealerContact = new iFruitContact("Arms Dealer");
            armsDealerContact.Answered += (contact) => OpenMainMenu();
            iFruit.Contacts.Add(armsDealerContact);

            Tick += OnTick;
        }

        private void OnTick(object sender, EventArgs e)
        {
            iFruit.Update();
            if (orderTimer != null && orderTimer.IsTimeUp)
            {
                SpawnDeliveryVehicle();
                orderTimer = null; // Timer Reset
            }

            int currentHour = Function.Call<int>(Hash.GET_CLOCK_HOURS);
            int currentMinute = Function.Call<int>(Hash.GET_CLOCK_MINUTES);

            TimeSpan currentTime = new TimeSpan(currentHour, currentMinute, 0);
            if (currentTime.Hours == 12 && currentTime.Minutes == 0 && lastStockUpdateTime != currentTime)
            {
                UpdateDealersStock(); // Update Available Stock
                lastStockUpdateTime = currentTime;
            }

            if (deliveryVehicle != null && Game.Player.Character.Position.DistanceTo(deliveryVehicle.Position) < 5.0f)
            {
                if (!Function.Call<bool>(Hash.IS_VEHICLE_DOOR_FULLY_OPEN, deliveryVehicle.Handle, 5))
                {
                    Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, deliveryVehicle.Handle, 5, false, false); // Open Vehicle Trunk
                }

                if (Game.IsControlJustPressed(GTA.Control.Context) && Game.Player.Character.Position.DistanceTo(deliveryVehicle.Position) < 2.0f)
                {
                    CollectOrder(); // Collect Order (Hotkey: E - Context Key)
                }
            }

            if (deliveryVehicle != null && !isOrderStolen)
            {
                TimeSpan timeSinceSpawn = DateTime.UtcNow - vehicleSpawnTime;
                if (timeSinceSpawn.TotalMinutes > 15)
                {
                    // Chance of theft every minute
                    if (timeSinceSpawn.TotalMinutes % 1 < 0.02) // Per minute
                    {
                        if (random.NextDouble() < 0.25) // 25% chance
                        {
                            OrderStolen();
                        }
                    }
                }
            }

            if (Game.Player.IsDead || Function.Call<bool>(GTA.Native.Hash.IS_PLAYER_BEING_ARRESTED, Game.Player, false))
            {
                if (orderTimer != null && !orderTimer.IsTimeUp)
                {
                    int refundAmount = currentOrder.Sum(item => item.Price);
                    RefundPlayer(refundAmount); // Refund
                    orderTimer.Reset(); // Timer Reset
                    currentOrder.Clear(); // Clear order

                    GTA.UI.Notification.PostTicker("You have died or been arrested. Your pending order has been canceled and you have been refunded.", false, false);
                }
                else if (deliveryVehicle != null && !isOrderStolen)
                {
                    deliveryVehicle.Delete(); // Clear delivery
                    deliveryBlip.Delete(); // Clear blip
                    currentOrder.Clear(); // Clear order

                    GTA.UI.Notification.PostTicker("You have died or been arrested. You have lost your order.", false, false);
                }
            }
        }

        private void OpenMainMenu()
        {
            mainMenu = new UIMenu("Arms Dealer", "Choose an option");

            var placeOrderItem = new UIMenuItem("Place Order", "Place an order for weapons");
            var viewStockItem = new UIMenuItem("View Stock", "View available stock and prices");
            var cancelOrderItem = new UIMenuItem("Cancel Order", "Cancel a pending order");

            mainMenu.AddItem(placeOrderItem);
            mainMenu.AddItem(viewStockItem);
            mainMenu.AddItem(cancelOrderItem);

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
            UIMenu ammoMenu = new UIMenu("Ammo Selection", "Select ammo for each weapon");

            // Add menu items for each weapon in the order
            foreach (var weapon in currentOrder)
            {
                var ammoItem = new UIMenuSliderItem(weapon.Name + " Ammo", "Select amount of ammo", true)
                {
                    Value = 0 // Default ammo amount
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

        private int CalculateAmmoCost(UIMenu ammoMenu)
        {
            int totalAmmoCost = 0;
            foreach (var item in ammoMenu.MenuItems.OfType<UIMenuSliderItem>())
            {
                int ammoAmount = item.Value;
                int costPerUnit = 5; // Assuming a fixed cost per unit of ammo
                totalAmmoCost += ammoAmount * costPerUnit;
            }
            return totalAmmoCost;
        }

        private void FinalizeOrder(int additionalCost)
        {
            int orderTotal = currentOrder.Sum(item => item.Price) + additionalCost;
            if (!ChargePlayer(orderTotal))
            {
                GTA.UI.Notification.PostTicker("You don't have enough money for this order.", false, false);
                return;
            }

            // Start order timer for 30 in-game minutes
            orderTimer = new GameTimer(30);
            GTA.UI.Notification.PostTicker("Your order will be ready in 30 minutes.", false, false);
        }

        private bool ChargePlayer(int amount)
        {
            if (Game.Player.Money >= amount)
            {
                Game.Player.Money -= amount;
                return true;
            }
            else
            {
                return false; // Not enough money
            }
        }

        private void CancelOrder()
        {
            if (orderTimer != null)
            {
                int refundAmount = currentOrder.Sum(item => item.Price);
                RefundPlayer(refundAmount);
                orderTimer.Reset(); // Timer Reset
                currentOrder.Clear(); // Clear order

                GTA.UI.Notification.PostTicker("Your order has been canceled. You have been refunded.", false, false);
            }
            else
            {
                GTA.UI.Notification.PostTicker("You either haven't placed an order or the delivery has already arrived.", false, false);
            }
        }

        private void RefundPlayer(int amount)
        {
            Game.Player.Money += amount;
        }

        private void SpawnDeliveryVehicle()
        {
            var randomLocation = deliveryLocations[random.Next(0, deliveryLocations.Count)];
            deliveryVehicle = World.CreateVehicle(new Model("adder"), randomLocation.Position);
            deliveryBlip = deliveryVehicle.AddBlip();
            deliveryBlip.ShowRoute = true; // Add GPS

            GTA.UI.Notification.PostTicker("Your order has arrived. It's marked on your GPS.", false, false);

            vehicleSpawnTime = DateTime.UtcNow;
            isOrderStolen = false;
        }

        private void CollectOrder()
        {
            foreach (var item in currentOrder)
            {
                GiveItemToPlayer(item);
            }

            GTA.UI.Notification.PostTicker("You have collected your order.", false, false);

            // Clear order
            currentOrder.Clear();

            // Clear delivery
            deliveryBlip.Delete();
            deliveryVehicle = null;
        }

        private void GiveItemToPlayer(WeaponItem item)
        {
            WeaponHash weaponHash = GetWeaponHashFromName(item.Name);
            Game.Player.Character.Weapons.Give(weaponHash, 1, true, true);
        }

        private WeaponHash GetWeaponHashFromName(string weaponName)
        {
            switch (weaponName)
            {
                case "Pistol":
                    return WeaponHash.Pistol;
                case "Assault Rifle":
                    return WeaponHash.AssaultRifle;
                // Add more cases for other weapon names
                default:
                    return WeaponHash.Unarmed;
            }
        }

        private void ViewAvailableStock()
        {
            string stockDetails = "Available Stock:\n";
            foreach (var item in GetAvailableStock())
            {
                stockDetails += $"{item.Name} - ${item.Price}\n"; // Get stock
            }

            GTA.UI.Notification.PostTicker(stockDetails, false, false);
        }


        private void UpdateDealersStock()
        {
            List<WeaponItem> newStock = new List<WeaponItem>();
            while (newStock.Count < 5)
            {
                WeaponItem randomWeapon = allPossibleWeapons[random.Next(allPossibleWeapons.Count)];
                if (!newStock.Contains(randomWeapon))
                {
                    newStock.Add(randomWeapon);
                }
            }

            availableStock = newStock;
        }

        private List<WeaponItem> GetAvailableStock()
        {
            return availableStock;
        }

        private void OrderStolen()
        {
            GTA.UI.Notification.PostTicker("Your order has been stolen!", false, false);

            // Clear the order
            currentOrder.Clear();

            // Clear delivery
            if (deliveryBlip != null)
            {
                deliveryBlip.Delete();
                deliveryBlip = null;
            }
            if (deliveryVehicle != null)
            {
                deliveryVehicle.Delete();
                deliveryVehicle = null;
            }

            isOrderStolen = true;
        }
    }

    public class Location
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }

        public Location(Vector3 position, float heading)
        {
            Position = position;
            Heading = heading;
        }
    }

    public class WeaponItem
    {
        public string Name { get; set; }
        public int Price { get; set; }

        public WeaponItem(string name, int price)
        {
            Name = name;
            Price = price;
        }
    }

    public class GameTimer
    {
        private DateTime lastCheckTime;
        private int durationInGameMinutes;

        public bool IsTimeUp => (DateTime.UtcNow - lastCheckTime).TotalSeconds >= durationInGameMinutes * 2;

        public GameTimer(int durationInGameMinutes)
        {
            this.durationInGameMinutes = durationInGameMinutes;
            lastCheckTime = DateTime.UtcNow;
        }

        public void Reset()
        {
            lastCheckTime = DateTime.UtcNow;
        }
    }
}
