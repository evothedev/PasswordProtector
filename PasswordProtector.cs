using System.Collections.Generic;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Password Protector", "Evo", "1.0.0")]
    [Description("Displays a GUI password prompt on join. Includes an in-game admin menu for complete configuration.")]
    class PasswordProtector : RustPlugin
    {
        private const string PermissionBypass = "passwordprotection.bypass";
        private const string PermissionAdmin = "passwordprotection.admin";

        private const string UILayerName = "PasswordAuthPanel";
        private const string AdminUILayerName = "PasswordAdminPanel";

        private readonly HashSet<ulong> authenticatedPlayers = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> loginAttempts = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, Vector3> frozenPositions = new Dictionary<ulong, Vector3>();

        private Configuration config;

        private class Configuration
        {
            public string ServerPassword = "SecretPassword123";
            public int MaxAttempts = 3;
            public bool AdminsBypass = false;
        }

        #region Configuration Lifecycles

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionBypass, this);
            permission.RegisterPermission(PermissionAdmin, this);
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || IsAuthenticated(player)) return;
            
            frozenPositions[player.userID] = player.transform.position;
            CreateAuthUI(player);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            authenticatedPlayers.Remove(player.userID);
            loginAttempts.Remove(player.userID);
            frozenPositions.Remove(player.userID);
        }

        private void OnPlayerInput(BasePlayer player, InputState inputState)
        {
            if (player == null || IsAuthenticated(player) || player.IsSleeping()) return;

            inputState.current.buttons = 0;
            inputState.current.aimAngles = player.viewAngles;
        }

        private object OnPlayerMove(BasePlayer player, InputState inputState)
        {
            if (player == null || IsAuthenticated(player) || player.IsSleeping()) return null;

            Vector3 frozenPos;
            if (frozenPositions.TryGetValue(player.userID, out frozenPos))
            {
                if (Vector3.Distance(player.transform.position, frozenPos) > 0.05f)
                {
                    player.Teleport(frozenPos);
                }
            }

            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer attacker = info?.InitiatorPlayer;
            if (attacker != null && !IsAuthenticated(attacker)) return true;
            return null;
        }

        private object CanInteractWithEntity(BasePlayer player, BaseEntity entity)
        {
            if (player != null && !IsAuthenticated(player)) return false;
            return null;
        }

        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (player == null || IsAuthenticated(player)) return null;
            return true;
        }

        private object OnServerCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || IsAuthenticated(player)) return null;

            if (arg.cmd?.FullName != null && arg.cmd.FullName.StartsWith("passwordprotection.")) return null;

            return true;
        }

        #endregion

        #region User UI Generation

        private void CreateAuthUI(BasePlayer player, string errorMessage = "")
        {
            CuiHelper.DestroyUi(player, UILayerName);

            if (!frozenPositions.ContainsKey(player.userID))
            {
                frozenPositions[player.userID] = player.transform.position;
            }

            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.95", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Hud", UILayerName);

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 0.8" },
                RectTransform = { AnchorMin = "0.35 0.35", AnchorMax = "0.65 0.65" }
            }, UILayerName, "MainBox");

            elements.Add(new CuiLabel
            {
                Text = { Text = "SERVER PASSWORD REQUIRED", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0.8 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0 0.75", AnchorMax = "1 0.95" }
            }, "MainBox");

            string displayMessage = string.IsNullOrEmpty(errorMessage) ? "Please enter the server password to play." : errorMessage;
            elements.Add(new CuiLabel
            {
                Text = { Text = displayMessage, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.05 0.55", AnchorMax = "0.95 0.7" }
            }, "MainBox");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.25 0.25 0.25 1" },
                RectTransform = { AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.45" }
            }, "MainBox", "InputFieldBg");

            elements.Add(new CuiElement
            {
                Parent = "InputFieldBg",
                Components =
                {
                    new CuiInputFieldComponent { NeedsKeyboard = true, IsPassword = true, Command = "passwordprotection.submit", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });

            elements.Add(new CuiLabel
            {
                Text = { Text = "Press [ENTER] after typing to submit.", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.5 1" },
                RectTransform = { AnchorMin = "0 0.05", AnchorMax = "1 0.2" }
            }, "MainBox");

            CuiHelper.AddUi(player, elements);
        }

        #endregion

        #region Admin UI Generation

        private void CreateAdminUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, AdminUILayerName);

            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.08 0.08 0.08 0.95", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Hud", AdminUILayerName);

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.13 0.13 0.13 0.95" },
                RectTransform = { AnchorMin = "0.25 0.2", AnchorMax = "0.75 0.8" }
            }, AdminUILayerName, "AdminBox");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.6 0.8 0.8" },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, "AdminBox", "AdminHeader");

            elements.Add(new CuiLabel
            {
                Text = { Text = "PASSWORD PROTECTOR CONFIGURATION DASHBOARD", FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, "AdminHeader");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.2 0.2 0.2 0.4" },
                RectTransform = { AnchorMin = "0.05 0.68", AnchorMax = "0.95 0.82" }
            }, "AdminBox", "RowBypass");

            elements.Add(new CuiLabel
            {
                Text = { Text = "ADMINS BYPASS GUI\n<size=11><color=#a0a0a0>If enabled, server administrators skip entering the password.</color></size>", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.6 1" }
            }, "RowBypass");

            string bypassBtnColor = config.AdminsBypass ? "0.2 0.6 0.2 1" : "0.6 0.2 0.2 1";
            string bypassBtnText = config.AdminsBypass ? "ENABLED" : "DISABLED";

            elements.Add(new CuiButton
            {
                Button = { Color = bypassBtnColor, Command = "passwordprotection.admin_togglebypass" },
                RectTransform = { AnchorMin = "0.65 0.2", AnchorMax = "0.95 0.8" },
                Text = { Text = bypassBtnText, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, "RowBypass");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.2 0.2 0.2 0.4" },
                RectTransform = { AnchorMin = "0.05 0.50", AnchorMax = "0.95 0.64" }
            }, "AdminBox", "RowAttempts");

            elements.Add(new CuiLabel
            {
                Text = { Text = "MAX FAILED ATTEMPTS\n<size=11><color=#a0a0a0>Adjust the amount of incorrect inputs before a player is kicked.</color></size>", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.6 1" }
            }, "RowAttempts");

            elements.Add(new CuiButton
            {
                Button = { Color = "0.3 0.3 0.3 1", Command = "passwordprotection.admin_adjustattempts -1" },
                RectTransform = { AnchorMin = "0.65 0.2", AnchorMax = "0.72 0.8" },
                Text = { Text = "-", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, "RowAttempts");

            elements.Add(new CuiLabel
            {
                Text = { Text = config.MaxAttempts.ToString(), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.74 0.2", AnchorMax = "0.86 0.8" }
            }, "RowAttempts");

            elements.Add(new CuiButton
            {
                Button = { Color = "0.3 0.3 0.3 1", Command = "passwordprotection.admin_adjustattempts 1" },
                RectTransform = { AnchorMin = "0.88 0.2", AnchorMax = "0.95 0.8" },
                Text = { Text = "+", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, "RowAttempts");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.2 0.2 0.2 0.4" },
                RectTransform = { AnchorMin = "0.05 0.32", AnchorMax = "0.95 0.46" }
            }, "AdminBox", "RowPassword");

            elements.Add(new CuiLabel
            {
                Text = { Text = $"SERVER PASSWORD\n<size=11><color=#a0a0a0>Current Password: </color><color=#55ff55>{config.ServerPassword}</color></size>", FontSize = 13, Align = TextAnchor.MiddleLeft, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0.03 0", AnchorMax = "0.55 1" }
            }, "RowPassword");

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 1" },
                RectTransform = { AnchorMin = "0.6 0.2", AnchorMax = "0.95 0.8" }
            }, "RowPassword", "AdminPasswordBg");

            elements.Add(new CuiElement
            {
                Parent = "AdminPasswordBg",
                Components =
                {
                    new CuiInputFieldComponent { NeedsKeyboard = true, IsPassword = false, Command = "passwordprotection.admin_setpassword", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });

            elements.Add(new CuiLabel
            {
                Text = { Text = "Type a new password inside the black input box and hit [ENTER] to save.", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.5 0.5 0.5 1" },
                RectTransform = { AnchorMin = "0.05 0.22", AnchorMax = "0.95 0.28" }
            }, "AdminBox");

            elements.Add(new CuiButton
            {
                Button = { Color = "0.6 0.2 0.2 0.9", Command = "passwordprotection.admin_close" },
                RectTransform = { AnchorMin = "0.3 0.06", AnchorMax = "0.7 0.15" },
                Text = { Text = "CLOSE ADMIN MENU", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" }
            }, "AdminBox");

            CuiHelper.AddUi(player, elements);
        }

        #endregion

        #region User UI Commands

        [ConsoleCommand("passwordprotection.submit")]
        private void CmdSubmitPassword(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || IsAuthenticated(player)) return;

            string enteredPassword = arg.GetString(0);

            if (enteredPassword == config.ServerPassword)
            {
                authenticatedPlayers.Add(player.userID);
                loginAttempts.Remove(player.userID);
                frozenPositions.Remove(player.userID);
                
                CuiHelper.DestroyUi(player, UILayerName);
                SendReply(player, "<color=#55ff55>Access Granted! Enjoy your stay.</color>");
            }
            else
            {
                int attempts;
                loginAttempts.TryGetValue(player.userID, out attempts);
                attempts++;
                loginAttempts[player.userID] = attempts;

                if (attempts >= config.MaxAttempts)
                {
                    CuiHelper.DestroyUi(player, UILayerName);
                    Network.Net.sv.Kick(player.net.connection, "Failed password verification.");
                }
                else
                {
                    string error = $"<color=#ff5555>Incorrect Password! ({attempts}/{config.MaxAttempts} attempts)</color>";
                    CreateAuthUI(player, error);
                }
            }
        }

        #endregion

        #region Admin UI & Chat Commands

        [ChatCommand("passgui")]
        private void CmdPassGui(BasePlayer player)
        {
            if (player == null) return;
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))
            {
                SendReply(player, "<color=#ff5555>You do not have permission to use this command.</color>");
                return;
            }

            CreateAdminUI(player);
        }

        [ConsoleCommand("passwordprotection.admin_togglebypass")]
        private void CmdAdminToggleBypass(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))) return;

            config.AdminsBypass = !config.AdminsBypass;
            SaveConfig();
            CreateAdminUI(player);
        }

        [ConsoleCommand("passwordprotection.admin_adjustattempts")]
        private void CmdAdminAdjustAttempts(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))) return;

            int offset = arg.GetInt(0, 0);
            config.MaxAttempts = Mathf.Clamp(config.MaxAttempts + offset, 1, 10);
            SaveConfig();
            CreateAdminUI(player);
        }

        [ConsoleCommand("passwordprotection.admin_setpassword")]
        private void CmdAdminSetPassword(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermissionAdmin))) return;

            string newPassword = arg.GetString(0);
            if (!string.IsNullOrEmpty(newPassword))
            {
                config.ServerPassword = newPassword.Trim();
                SaveConfig();
                SendReply(player, $"<color=#10c0ff>[Admin Setup]</color> Server password has been changed to: <color=#55ff55>{config.ServerPassword}</color>");
            }
            CreateAdminUI(player);
        }

        [ConsoleCommand("passwordprotection.admin_close")]
        private void CmdAdminClose(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player == null) return;

            CuiHelper.DestroyUi(player, AdminUILayerName);
        }

        #endregion

        #region Helpers

        private bool IsAuthenticated(BasePlayer player)
        {
            if (config.AdminsBypass && player.IsAdmin) return true;
            if (permission.UserHasPermission(player.UserIDString, PermissionBypass)) return true;
            return authenticatedPlayers.Contains(player.userID);
        }

        #endregion
    }
}