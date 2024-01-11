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

        private List<Vector3> deliveryLocations = new List<Vector3>
        {
            new Vector3(123.456f, 789.012f, 45.678f), // Add more delivery locations as needed
            // Add additional delivery locations here...
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
            int orderTotal = currentOrder.Sum(item => item.Price);
            if (currentOrder.Count == 0)
            {
                GTA.UI.Notification.PostTicker("Your order is empty!", false, false);
                return;
            }
            else if (!ChargePlayer(orderTotal))
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
            deliveryVehicle = World.CreateVehicle(new Model("adder"), randomLocation);
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
