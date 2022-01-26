using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Banking", "varygoode", "1.0.0")]
	[Description("Simple banking plugin for use with Economics")]

	internal class Banking : CovalencePlugin
	{
		#region Fields

		private const string PermAdmin = "banking.admin";
        private const string PermStimulus = "banking.stimulus";
        private const string PermFreeze = "banking.freeze";
        private const string PermAudit = "banking.audit";
        private const string PermUse = "banking.use";

        [PluginReference]
        private Plugin Economics;

        private StoredData storedData;
        private Configuration config;
        private double currentAcctID;

		#endregion Fields

		#region Init

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermStimulus, this);
            permission.RegisterPermission(PermFreeze, this);
            permission.RegisterPermission(PermAudit, this);
            permission.RegisterPermission(PermUse, this);

            currentAcctID = -1;

            storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            var lastAccount = storedData.Accounts.Values.Select(l => l.Last()).OrderByDescending(t => t.AccountID).FirstOrDefault();
            Account.CurrentID = lastAccount != null ? (lastAccount.AccountID > 8 ? lastAccount.AccountID : 9) : 0;

            CreateDefaultAccounts();
            UpdateOwnerNames();
        }

        #endregion Init

        #region Hooks

        private void Loaded()
        {
            UpdateOwnerNames();
        }

        private void OnServerSave()
        {
            UpdateOwnerNames();
            SaveData();
        }

        private void Unload() => SaveData();

        #endregion Hooks

        #region Commands

        [Command("bank")]
        private void CommandBank(IPlayer iPlayer, string command, string[] args)
        {
        	if (!iPlayer.HasPermission(PermUse) && !iPlayer.HasPermission(PermAdmin))
            {
                iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                return;
            }

            if (args.Length < 1)
            {
                var message = "Usage: /bank info";
                if (iPlayer.HasPermission(PermAdmin) || iPlayer.HasPermission(PermUse)) message += "|list|view|edit|open|close|deposit|withdraw|transfer";
                if (iPlayer.HasPermission(PermAdmin) || iPlayer.HasPermission(PermStimulus)) message += "|stimulus";
                if (iPlayer.HasPermission(PermAdmin) || iPlayer.HasPermission(PermFreeze)) message += "|freeze|unfreeze";
                if (iPlayer.HasPermission(PermAdmin)) message += "|defaults|wipe";

                iPlayer.Message(message);
                return;
            }

            switch (args[0].ToLower())
            {
                case "info":
                    iPlayer.Reply(Lang("Info1", iPlayer.Id, command));
                    iPlayer.Reply(Lang("Info2", iPlayer.Id, command));
                    iPlayer.Reply(Lang("Info3", iPlayer.Id, command));
                    iPlayer.Reply(Lang("Info4", iPlayer.Id, command));
                    iPlayer.Reply(Lang("Info5", iPlayer.Id, command));
                    iPlayer.Reply(Lang("Info6", iPlayer.Id, command));

                    if(iPlayer.HasPermission(PermAdmin) || iPlayer.HasPermission(PermStimulus))
                    {
                        iPlayer.Reply(Lang("Info7", iPlayer.Id, command));
                    }

                    if(iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("Info8", iPlayer.Id, command));
                    }

                    return;

                case "list":
                    var yourAccounts = FindAccountsWithOwnerID(iPlayer.Id);
                    var sharedAccounts = FindAccountsWithAccessor(iPlayer.Id);
                    if(yourAccounts.IsEmpty() && sharedAccounts.IsEmpty())
                    {
                        iPlayer.Reply(Lang("NoAccts", iPlayer.Id, command));
                        return;
                    }

                    string acctsOutput = (yourAccounts.IsEmpty()) ? "" : Lang("Account_List_Header_Owned", iPlayer.Id, command) + "\n";
                    if(!yourAccounts.IsEmpty())
                    {
                        foreach (var acct in yourAccounts)
                        {
                            acctsOutput += Lang("Account_List_Separator", iPlayer.Id, command) + "\n";
                            acctsOutput += Lang("Account_List_Entry", iPlayer.Id, acct.AccountID, acct.AccountName, acct.Balance) + "\n";
                            if (acct.IsFrozen) acctsOutput += "<color=#FF0000>[FROZEN]</color>\n";
                        }
                    }

                    if(!sharedAccounts.IsEmpty())
                    {
                        acctsOutput += Lang("Account_List_Separator", iPlayer.Id, command) + "\n";
                        acctsOutput += Lang("Account_List_Header_Accessible", iPlayer.Id, command) + "\n";

                        foreach (var acct in sharedAccounts)
                        {
                            acctsOutput += Lang("Account_List_Separator", iPlayer.Id, command) + "\n";
                            acctsOutput += Lang("Account_List_Entry", iPlayer.Id, acct.AccountID, acct.AccountName, acct.Balance) + "\n";
                            if (acct.IsFrozen) acctsOutput += "<color=#FF0000>[FROZEN]</color>\n";
                        }
                    }
                    iPlayer.Reply(acctsOutput);

                    return;

                case "view":
                    if(args.Length < 2)
                    {
                        iPlayer.Reply(Lang("Account_View_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account viewAcct = FindAccountWithID(args[1]);

                    if (viewAcct == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, command));
                        return;
                    }

                    if(!viewAcct.Accessors.Contains(iPlayer.Id) && !iPlayer.HasPermission(PermAdmin) && !iPlayer.HasPermission(PermAudit))
                    {
                        string noAccessReply = Lang("NoAccess", iPlayer.Id, args[1]);
                        if(viewAcct.IsDefault) noAccessReply += "This is a <color=#FF0000>default</color> account. Edit config to change the owner.";
                        iPlayer.Reply(noAccessReply);
                        return;
                    }

                    if (viewAcct.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, viewAcct.AccountID));
                        return;
                    }

                    string viewReply = Lang("Account_View_Info", iPlayer.Id, viewAcct.AccountID, viewAcct.AccountName, viewAcct.OwnerName, viewAcct.Balance) + "\n";

                    string accessors = "The following people have access to this account:\n";
                    foreach(string accessor in viewAcct.Accessors)
                    {
                        BasePlayer accessorPlayer = GetAnyPlayerByUserID(accessor);
                        if(accessorPlayer == null) continue;
                        accessors += accessorPlayer.displayName + "\n";
                    }

                    string transactions = viewAcct.Transactions.IsEmpty() ? "" : "--------------------\n<color=#00D8D8>TRANSACTIONS</color>\n--------------------\n";
                    double earliestTransactionID = 0;
                    if (!viewAcct.Transactions.IsEmpty())
                    {
                        earliestTransactionID = viewAcct.Transactions.First().TransactionID - 5;
                        if (args.Length == 3)
                        {
                            earliestTransactionID = Convert.ToDouble(args[2]);
                        }
                    }
                    foreach(var transaction in viewAcct.Transactions)
                    {
                        if (transaction.TransactionID > earliestTransactionID)
                        {
                            transactions += transaction.TransactionLine() + "\n--------------------\n";
                        }
                    }

                    iPlayer.Reply(viewReply + accessors + transactions);

                    currentAcctID = viewAcct.AccountID;

                    return;

                case "edit":
                    if(args.Length < 4)
                    {
                        iPlayer.Reply(Lang("Account_Edit_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account editAccount = FindAccountWithID(args[1]);

                    if (editAccount == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, command));
                        return;
                    }

                    if (!editAccount.Accessors.Contains(iPlayer.Id) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoAccess", iPlayer.Id, args[1]));
                        return;
                    }

                    if (editAccount.OwnerID != iPlayer.Id && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("Account_Edit_NotOwner", iPlayer.Id, args[1]));
                        return;
                    }

                    AccountEditField field;
                    if(!AccountEditField.TryParse(args[2].ToUpper(), out field))
                    {
                        iPlayer.Reply(Lang("Account_Edit_Usage", iPlayer.Id, command));
                        return;
                    }

                    if (editAccount.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, editAccount.AccountID));
                        return;
                    }

                    if (!EditAccount(editAccount, field, args[3]))
                    {
                        iPlayer.Reply(Lang("Account_Edit_Failure", iPlayer.Id, command, editAccount.AccountID));
                        return;
                    }

                    switch(field)
                    {
                        case AccountEditField.NAME:
                            iPlayer.Reply(Lang("Account_Edit_NameSuccess", iPlayer.Id, editAccount.AccountID, editAccount.AccountName));

                            return;

                        case AccountEditField.OWNER:
                            iPlayer.Reply(Lang("Account_Edit_OwnerSuccess", iPlayer.Id, editAccount.AccountID, editAccount.OwnerName));

                            return;

                        case AccountEditField.ACCESSOR_ADD:
                            iPlayer.Reply(Lang("Account_Edit_AccessorAddSuccess", iPlayer.Id, (FindPlayer(args[3]).Object as BasePlayer).displayName, editAccount.AccountID));

                            return;

                        case AccountEditField.ACCESSOR_REMOVE:
                            iPlayer.Reply(Lang("Account_Edit_AccessorRemoveSuccess", iPlayer.Id, (FindPlayer(args[3]).Object as BasePlayer).displayName, editAccount.AccountID));

                            return;
                    }

                    return;

                case "open":
                    if(args.Length < 2)
                    {
                        iPlayer.Reply(Lang("Account_Open_Usage", iPlayer.Id, command));
                        return;
                    }

                    List<Account> openAccounts = FindAccountsWithOwnerID(iPlayer.Id);

                    foreach(Account acct in openAccounts)
                    {
                        if (acct.IsFrozen)
                        {
                            iPlayer.Reply(Lang("Account_Open_Frozen", iPlayer.Id, command));
                            return;
                        }
                    }

                    Account openAccount = FindAccountWithName(args[1]);
                    if (openAccount != null)
                    {
                        if (openAccount.OwnerID == iPlayer.Id)
                        {
                            iPlayer.Reply(Lang("Account_Open_Duplicate", iPlayer.Id, openAccount.AccountID));
                            return;
                        }

                        foreach(string name in config.defaultAccounts)
                        {
                            if (name.ToLower() == openAccount.AccountName.ToLower())
                            {
                                iPlayer.Reply(Lang("Account_Open_Prohibited", iPlayer.Id, command));
                                return;
                            }
                        }
                    }
                    
                    var acctNew = new Account(iPlayer.Id, args[1], (iPlayer.Object as BasePlayer).displayName, 0);

                    if (storedData.Accounts.ContainsKey(iPlayer.Id))
                    {
                        storedData.Accounts[iPlayer.Id].Add(acctNew);
                    }
                    else
                    {
                        storedData.Accounts.Add(iPlayer.Id, new List<Account>() { acctNew });
                    }

                    iPlayer.Reply(Lang("Account_Open_Success", iPlayer.Id, acctNew.AccountID));

                    return;

                case "close":
                    if(args.Length < 2)
                    {
                        iPlayer.Reply(Lang("Account_Close_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account acctToDelete = FindAccountWithID(args[1]);

                    if (acctToDelete == null)
                    {
                        iPlayer.Reply(Lang("Account_Close_NotFound", iPlayer.Id, args[1]));
                        return;
                    }
                    
                    if (acctToDelete.OwnerID != iPlayer.Id && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("Account_Close_NotOwner", iPlayer.Id, args[1]));
                        return;
                    }

                    if (acctToDelete.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, acctToDelete.AccountID));
                        return;
                    }
                    
                    if (acctToDelete.Balance > 0 && !Economics.Call<bool>("Deposit", iPlayer.Id, acctToDelete.Withdraw(acctToDelete.Balance, "CLOSING ACCOUNT")))
                    {
                        iPlayer.Reply(Lang("TransactionFailure", iPlayer.Id, command));
                        return;
                    }

                    iPlayer.Reply(Lang("Account_Close_Success", iPlayer.Id, acctToDelete.AccountID));
                    storedData.Accounts[acctToDelete.OwnerID].Remove(acctToDelete);

                    return;

                case "deposit":
                    if((args.Length < 2) || (args.Length < 3 && currentAcctID < 0))
                    {
                        iPlayer.Reply(Lang("Account_Deposit_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account depositAcct = (args.Length < 3) ? FindAccountWithID($"{currentAcctID}") : FindAccountWithID(args[2]);

                    if (depositAcct == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, command));
                        return;
                    }

                    if(!depositAcct.Accessors.Contains(iPlayer.Id) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoAccess", iPlayer.Id, depositAcct.AccountID));
                        return;
                    }

                    if (depositAcct.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, depositAcct.AccountID));
                        return;
                    }

                    double depositAmount = Convert.ToDouble(args[1]);
                    double pocketBalance = Economics.Call<double>("Balance", iPlayer.Id);

                    if (depositAmount > pocketBalance)
                    {
                        iPlayer.Reply(Lang("Account_Deposit_InsufficientFunds", iPlayer.Id, command));
                        return;
                    }

                    if (!Economics.Call<bool>("Withdraw", iPlayer.Id, depositAmount))
                    {
                        iPlayer.Reply(Lang("TransactionFailure", iPlayer.Id, command));
                        return;
                    }

                    depositAcct.Deposit(depositAmount, "COIN DEPOSIT");
                    iPlayer.Reply(Lang("Account_Deposit_Success", iPlayer.Id, depositAmount, depositAcct.AccountID));

                    return;

                case "withdraw":
                    if((args.Length < 2) || (args.Length < 3 && currentAcctID < 0))
                    {
                        iPlayer.Reply(Lang("Account_Withdraw_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account withdrawAcct = (args.Length < 3) ? FindAccountWithID($"{currentAcctID}") : FindAccountWithID(args[2]);

                    if (withdrawAcct == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, command));
                        return;
                    }

                    if(!withdrawAcct.Accessors.Contains(iPlayer.Id) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoAccess", iPlayer.Id, withdrawAcct.AccountID));
                        return;
                    }

                    if (withdrawAcct.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, withdrawAcct.AccountID));
                        return;
                    }

                    if (withdrawAcct.Balance <= 0)
                    {
                        iPlayer.Reply(Lang("InsufficientFunds", iPlayer.Id, command));
                        return;
                    }

                    double withdrawAmount = withdrawAcct.Withdraw(Convert.ToDouble(args[1]), "COIN WITHDRAWAL");

                    if (!Economics.Call<bool>("Deposit", iPlayer.Id, withdrawAmount))
                    {
                        iPlayer.Reply(Lang("TransactionFailure", iPlayer.Id, command));
                        return;
                    }

                    iPlayer.Reply(Lang("Account_Withdraw_Success", iPlayer.Id, withdrawAmount, withdrawAcct.AccountID));

                    return;

                case "transfer":
                    if(args.Length < 4)
                    {
                        iPlayer.Reply(Lang("Transfer_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account fromAccount = FindAccountWithID(args[2]);
                    Account toAccount = FindAccountWithID(args[3]);
                    double amount = Convert.ToDouble(args[1]);

                    if (fromAccount == null || toAccount == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, args[2]));
                        return;
                    }
                    
                    if (!fromAccount.Accessors.Contains(iPlayer.Id) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("Transfer_NoAccess", iPlayer.Id, args[2]));
                        return;
                    }

                    if (fromAccount.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, fromAccount.AccountID));
                        return;
                    }

                    if (toAccount.IsFrozen)
                    {
                        iPlayer.Reply(Lang("Frozen", iPlayer.Id, toAccount.AccountID));
                        return;
                    }

                    double amountTransferred = fromAccount.Withdraw(amount, $"TRANSFER TO ACCT #{toAccount.AccountID}");
                    
                    if (amountTransferred <= 0)
                    {
                        iPlayer.Reply(Lang("TransactionFailure", iPlayer.Id, command));
                        return;
                    }

                    toAccount.Deposit(amountTransferred, $"TRANSFER FROM ACCT #{fromAccount.AccountID}");
                    iPlayer.Reply(Lang("Transfer_Success", iPlayer.Id, amountTransferred, fromAccount.AccountID, toAccount.AccountID));

                    return;

                case "stimulus":
                    if (!iPlayer.HasPermission(PermStimulus) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                        return;
                    }

                    if (args.Length < 3)
                    {
                        iPlayer.Reply(Lang("Stimulus_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account stimulusAccount = FindAccountWithID(args[2]);

                    if (stimulusAccount == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, args[2]));
                        return;
                    }

                    if (args.Length < 4 || !args[3].Equals("CONFIRM"))
                    {
                        iPlayer.Reply(Lang("Stimulus_Warning", iPlayer.Id, args[1], args[2]));
                        return;
                    }

                    List<Account> oldestAccountOfEachPlayer = FindOldestAccountOfEachPlayer();

                    if ((oldestAccountOfEachPlayer.Count * Convert.ToDouble(args[1])) > stimulusAccount.Balance)
                    {
                        iPlayer.Reply(Lang("Stimulus_Failure", iPlayer.Id, stimulusAccount.AccountID));
                        return;
                    }

                    foreach (Account acct in oldestAccountOfEachPlayer)
                    {
                        acct.Deposit(FindAccountWithID(args[2]).Withdraw(Convert.ToDouble(args[1]), "STIMULUS TRANSFER"), "STIMULUS DEPOSIT");
                        BasePlayer player = GetAnyPlayerByUserID(acct.OwnerID);
                        if(player.IPlayer != null)
                        {
                            player.IPlayer.Reply(Lang("Stimulus_Received", player.UserIDString, args[1], acct.AccountID));
                        }
                    }

                    iPlayer.Reply(Lang("Stimulus_Success", iPlayer.Id, args[1], args[2]));

                    return;

                case "defaults":
                    if(!iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                        return;
                    }

                    if(FindAccountsWithOwnerID(config.defaultOwnerID).IsEmpty())
                    {
                        iPlayer.Reply("There are no default accounts. These can be set in the config.");
                        return;
                    }

                    if(args.Length == 2 && args[1].ToLower() == "reset")
                    {
                        ResetDefaultAccounts();
                        iPlayer.Reply("Default accounts have been successfully reset as per config.");
                        return;
                    }

                    string acctsDefault = Lang("Account_List_Header_Default", iPlayer.Id, command) + "\n";
                    foreach (var acct in FindAccountsWithOwnerID(config.defaultOwnerID))
                    {
                        acctsDefault += Lang("Account_List_Separator", iPlayer.Id, command) + "\n";
                        acctsDefault += Lang("Account_List_Entry", iPlayer.Id, acct.AccountID, acct.AccountName, acct.Balance) + "\n";
                    }
                    
                    iPlayer.Reply(acctsDefault);

                    return;

                case "wipe":
                    if (!iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                        return;
                    }

                    if (args.Length < 2 || !args[1].Equals("CONFIRM"))
                    {
                        iPlayer.Reply(Lang("Wipe_Warning", iPlayer.Id, command));
                        return;
                    }

                    storedData.Clear();
                    Account.CurrentID = 9;
                    ResetDefaultAccounts();
                    SaveData();
                    iPlayer.Reply(Lang("Wipe_Success", iPlayer.Id, command));

                    return;

                case "freeze":
                    if (!iPlayer.HasPermission(PermFreeze) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                        return;
                    }

                    if (args.Length < 2)
                    {
                        iPlayer.Reply(Lang("Freeze_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account acctToFreeze = FindAccountWithID(args[1]);

                    if (acctToFreeze == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, args[1]));
                        return;
                    }

                    acctToFreeze.IsFrozen = true;
                    iPlayer.Reply(Lang("Freeze_Success", iPlayer.Id, args[1]));

                    return;

                case "unfreeze":
                    if (!iPlayer.HasPermission(PermFreeze) && !iPlayer.HasPermission(PermAdmin))
                    {
                        iPlayer.Reply(Lang("NoUse", iPlayer.Id, command));
                        return;
                    }

                    if (args.Length < 2)
                    {
                        iPlayer.Reply(Lang("Unfreeze_Usage", iPlayer.Id, command));
                        return;
                    }

                    Account acctToUnfreeze = FindAccountWithID(args[1]);

                    if (acctToUnfreeze == null)
                    {
                        iPlayer.Reply(Lang("NoSuchAcct", iPlayer.Id, args[1]));
                        return;
                    }

                    acctToUnfreeze.IsFrozen = false;
                    iPlayer.Reply(Lang("Unfreeze_Success", iPlayer.Id, args[1]));

                    return;
            }
        }

        #endregion Commands

        #region Methods

        private bool EditAccount(Account account, AccountEditField field, string value)
        {
            switch (field)
            {
                case AccountEditField.NAME:
                    account.AccountName = value.ToUpper();
                    return true;

                case AccountEditField.OWNER:
                    IPlayer owner = FindPlayer(value);
                    if (owner == null) return false;
                    account.OwnerName = (owner.Object as BasePlayer).displayName;
                    account.OwnerID = owner.Id;
                    return true;

                case AccountEditField.ACCESSOR_ADD:
                    IPlayer accessorToAdd = FindPlayer(value);
                    if (accessorToAdd == null) return false;
                    return account.AddAccessor(accessorToAdd.Id);

                case AccountEditField.ACCESSOR_REMOVE:
                    IPlayer accessorToRemove = FindPlayer(value);
                    if (accessorToRemove == null) return false;
                    return account.RemoveAccessor(accessorToRemove.Id);
            }
            return false;
        }

        private void UpdateOwnerNames()
        {
            foreach (var player in BasePlayer.allPlayerList)
            {
                foreach(Account account in FindAccountsWithOwnerID(player.UserIDString))
                {
                    account.OwnerName = GetAnyPlayerByUserID(account.OwnerID).displayName;
                }
            }
        }

        private void CreateDefaultAccounts()
        {
            if (config == null || config.defaultAccounts.IsEmpty()) return;

            foreach (string name in config.defaultAccounts)
            {
                var defaultAcct = FindAccountWithName(name);

                if (defaultAcct == null)
                {
                    var acctNew = new Account(config.defaultOwnerID, name, "DEFAULT", 0, true);

                    if (storedData.Accounts.ContainsKey(config.defaultOwnerID))
                    {
                        if(Account.CurrentDefaultID < 10)
                        {
                            storedData.Accounts[config.defaultOwnerID].Add(acctNew);
                        }
                    }
                    else
                    {
                        storedData.Accounts.Add(config.defaultOwnerID, new List<Account>() { acctNew });
                    }
                }
                else
                {
                    Account.CurrentDefaultID++;
                }
            }
        }

        private void ResetDefaultAccounts()
        {
            var query = from outer in storedData.Accounts
                        from inner in outer.Value
                        where inner.IsDefault == true
                        select inner;

            List<Account> accounts = new List<Account>();

            foreach(var q in query)
            {
                accounts.Add(q);
            }

            foreach(Account acct in accounts)
            {
                storedData.Accounts[config.defaultOwnerID].Remove(acct);
            }

            Account.CurrentDefaultID = 0;

            CreateDefaultAccounts();
        }

        #endregion Methods

        #region API

        #endregion API

        #region Helpers

        private IPlayer GetActivePlayerByUserID(string userID)
        {
            foreach (var player in players.Connected)
                if (player.Id == userID) return player;
            return null;
        }

        public BasePlayer GetAnyPlayerByUserID(string userID)
        {
            foreach (var player in BasePlayer.allPlayerList)
                if (player.UserIDString == userID) return player;
            return null;
        }

        public IPlayer FindPlayer(string nameOrId)
        {
            foreach (var activePlayer in BasePlayer.allPlayerList)
            {
                if (activePlayer.UserIDString == nameOrId)
                    return activePlayer.IPlayer;
                if (activePlayer.displayName.ToLower() == nameOrId.ToLower())
                    return activePlayer.IPlayer;
            }

            return null;
        }

        private float GenericDistance(GenericPosition a, GenericPosition b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }

        private Account FindAccountWithName(string name)
        {
            var query = from outer in storedData.Accounts
                        from inner in outer.Value
                        where inner.AccountName.ToLower() == name.ToLower()
                        select inner;

            if (!query.Any()) return null;
            return query.First();
        }

        private Account FindAccountWithID(string ID)
        {
            var query = from outer in storedData.Accounts
                        from inner in outer.Value
                        where inner.AccountID.ToString() == ID
                        select inner;

            if (!query.Any()) return null;
            return query.First();
        }

        private List<Account> FindAccountsWithOwnerID(string ownerID)
        {
            var query = from outer in storedData.Accounts
                        from inner in outer.Value
                        where inner.OwnerID.ToString() == ownerID
                        select inner;

            List<Account> accounts = new List<Account>();

            if (!query.Any()) return accounts;

            foreach(var q in query)
            {
                accounts.Add(q);
            }
            
            return accounts;
        }

        private List<Account> FindAccountsWithAccessor(string accessorID)
        {
            var query = from outer in storedData.Accounts
                        from inner in outer.Value
                        where inner.Accessors.Contains(accessorID)
                        select inner;

            List<Account> accounts = new List<Account>();

            if (!query.Any()) return accounts;

            foreach(var q in query)
            {
                if(q.OwnerID != accessorID) accounts.Add(q);
            }

            return accounts;
        }

        private List<Account> FindOldestAccountOfEachPlayer()
        {
            List<Account> oldestAccountOfEachPlayer = new List<Account>();

            foreach (var player in BasePlayer.allPlayerList)
            {
                List<Account> playerAccounts = FindAccountsWithOwnerID(player.UserIDString);

                if(!playerAccounts.IsEmpty())
                {
                    oldestAccountOfEachPlayer.Add(playerAccounts.First());
                }
            }

            return oldestAccountOfEachPlayer;
        }

        #endregion Helpers

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Default Accounts (Up to 9)")]
            public string[] defaultAccounts =
            {
                "GOVERNMENT", "POLICE", "HEALTH", "BUILDINGS"
            };

            [JsonProperty("Default Accounts Owner ID")]
            public string defaultOwnerID = "0";

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    Puts("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                Puts($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Puts($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Data

        private class StoredData
        {
            public Dictionary<string, List<Account>> Accounts = new Dictionary<string, List<Account>>();

            public StoredData()
            {
            }

            public void Clear()
            {
                Accounts.Clear();                
            }
        }

        private class Account
        {
            public static double CurrentID = 9;
            public static double CurrentDefaultID = 0;

            public double AccountID { get; set; }
            public string OwnerID { get; set; }
            public string AccountName { get; set; }
            public string OwnerName { get; set; }
            public List<string> Accessors { get; set; }
            public double Balance { get; set; }
            public bool IsDefault { get; set; }
            public bool IsFrozen { get; set; }
            public List<Transaction> Transactions { get; set; }

            [JsonConstructor]
            public Account(double accountID, string ownerID, string accountName, string ownerName, double balance, bool isDefault)
            {
                if (accountID.ToString().Contains("69") || accountID.ToString().Contains("420")) ++accountID;
                AccountID = accountID;
                OwnerID = ownerID;
                AccountName = accountName.ToUpper();
                OwnerName = ownerName;
                Balance = balance;
                IsDefault = isDefault;
                IsFrozen = false;
                Accessors = new List<string>();
                Accessors.Add(ownerID);
                Transactions = new List<Transaction>();
            }

            public Account(string ownerID, string accountName, string ownerName, double balance) : this(++CurrentID, ownerID, accountName, ownerName, balance, false)
            {
            }

            public Account(string ownerID, string accountName, string ownerName, double balance, bool isDefault) : this(++CurrentDefaultID, ownerID, accountName, ownerName, balance, isDefault)
            {
            }

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(JsonConvert.SerializeObject(this));

            public bool AddAccessor(string accessor)
            {               
                if (!Accessors.Contains(accessor))
                {
                    Accessors.Add(accessor);
                    return true;
                }

                return false;
            }

            public bool RemoveAccessor(string accessor)
            {
                return Accessors.Remove(accessor);
            }

            public double Withdraw(double amt, string reason)
            {
                double withdrawalAmount = ((Balance - amt) > 0) ? amt : Balance;
                Balance -= withdrawalAmount;
                double transactionID = Transactions.IsEmpty() ? 1 : Transactions.First().TransactionID + 1;
                Transactions.Insert(0, new Transaction(transactionID, "<color=#FF0000>-$"+$"{withdrawalAmount}"+"</color>", reason));
                return withdrawalAmount;
            }

            public void Deposit(double amt, string reason)
            {
                Balance += amt;
                double transactionID = Transactions.IsEmpty() ? 1 : Transactions.First().TransactionID + 1;
                Transactions.Insert(0, new Transaction(transactionID, "<color=#66FF00>+$"+$"{amt}"+"</color>", reason));
            }

            public void EditAcctName(string newName)
            {
                AccountName = newName;
            }

            public class Transaction
            {
                public double TransactionID { get; set; }
                public string Amount { get; set; }
                public string Description { get; set; }

                [JsonConstructor]
                public Transaction(double transactionID, string amount, string description)
                {
                    TransactionID = transactionID;
                    Amount = amount;
                    Description = description;
                }

                public string TransactionLine()
                {
                    return $"Transaction #{TransactionID} \t Amount: {Amount}\nDETAILS: [{Description}]";
                }
            }
        }

        private enum AccountEditField
        {
            NAME,
            OWNER,
            ACCESSOR_ADD,
            ACCESSOR_REMOVE
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

        #endregion Data

        #region Localization

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoUse"] = "You are not permitted to use that command.",
                ["DefaultAccount"] = "You are not permitted to create an account with the same name as a default account.",
                ["NoAccts"] = "You currently have no open accounts. To open an account, use /banking open.",
                ["NoSuchAcct"] = "No such account exists.",
                ["NoAccess"] = "You cannot access account #{0}. Contact the owner to gain access to the account.",
                ["InsufficientFunds"] = "You have insufficient funds to complete the transaction.",
                ["TransactionFailure"] = "Your transaction failed to process.",
                ["Frozen"] = "Account #{0} is <color=#FF0000>FROZEN</color>. Contact the police to unfreeze it.",

                ["Info1"] = "Use '/bank list' to view all your accounts.",
                ["Info2"] = "Use '/bank view Account# <optional:Transaction#>' to view the account with id Account# and all transactions from Transaction# forward.",
                ["Info3"] = "Use '/bank edit Account# FieldName NewValue' to edit account details.\nField Names: NAME, OWNER, ACCESSOR_ADD, ACCESSOR_REMOVE",
                ["Info4"] = "Use '/bank open AccountName' or '/bank close Account#' to open a new account or close a specific account with id Account#.",
                ["Info5"] = "Use '/bank deposit Amount# Account#' or '/bank withdraw Amount# Account#' to deposit fund into or withdraw amt# funds from account with id Account#.",
                ["Info6"] = "Use '/bank transfer Amount# Acct# Acct#' to transfer Amount# funds from the first Acct# to the second Acct#.",
                ["Info7"] = "Use '/bank stimulus Amount# Account#' to deposit Amount# funds from acct# into the oldest account of each player who owns at least 1 account.",
                ["Info8"] = "Use '/bank wipe' to remove ALL account data. [USE CAUTION!!! THIS CANNOT BE UNDONE!!!]",
                ["Info9"] = "Use '/bank freeze Account#'' to freeze all of that account's activity. Use '/bank unfreeze Account#' to reverse this.",

                ["Account_List_Header_Default"] = "<color=#00D8D8>DEFAULT ACCOUNTS</color>",

                ["Account_List_Header_Owned"] = "<color=#00D8D8>OWNED ACCOUNTS</color>",
                ["Account_List_Header_Accessible"] = "<color=#00D8D8>ACCESSIBLE ACCOUNTS</color>",
                ["Account_List_Separator"] = "-----------------------------",
                ["Account_List_Entry"] = "ACCOUNT #{0}, {1} || BALANCE: <color=#66FF00>${2}</color>",

                ["Account_View_Usage"] = "Usage: /bank view Account# <optional:Transaction#>",
                ["Account_View_Info"] = "You are viewing account #{0}, {1}, owned by {2}.\nThe account balance is currently <color=#66FF00>${3}</color>.",

                ["Account_Edit_Usage"] = "Usage: /bank edit Account# FieldName NewValue\nField Names: NAME, OWNER, ACCESSOR_ADD, ACCESSOR_REMOVE",
                ["Account_Edit_NotOwner"] = "You must own the account to edit it.",
                ["Account_Edit_Failure"] = "You have failed to edit account #{0}.",
                ["Account_Edit_NameSuccess"] = "You have successfully changed the NAME of account #{0} to {1}.",
                ["Account_Edit_OwnerSuccess"] = "You have successfully changed the OWNER of account #{0} to {1}.",
                ["Account_Edit_AccessorAddSuccess"] = "You have successfully added {0} to the accessor list of account #{1}.",
                ["Account_Edit_AccessorRemoveSuccess"] = "You have successfully removed {0} from the accessor list of account #{1}.",

                ["Account_Open_Usage"] = "Usage: /bank open AccountName",
                ["Account_Open_Duplicate"] = "You already have an account with that name. To access that account, use account #{0}.",
                ["Account_Open_Frozen"] = "You currently have a frozen account and cannot open new accounts.",
                ["Account_Open_Prohibited"] = "Account name is prohibited.",
                ["Account_Open_Success"] = "You have successfully opened your account. To access that account, use account #{0}.",

                ["Account_Close_Usage"] = "Usage: /bank close Account#",
                ["Account_Close_NotFound"] = "You don't own an account with account #{0}.",
                ["Account_Close_NotOwner"] = "You don't own account #{0}. Contact the owner to close the account.",
                ["Account_Close_Success"] = "You have successfully closed account #{0}.",

                ["Account_Deposit_Usage"] = "Usage: /bank deposit Amount# Account#\nIf you recently viewed an account, you may omit Account#.",
                ["Account_Deposit_InsufficientFunds"] = "You don't have enough coin in your pocket for such a deposit.",
                ["Account_Deposit_Success"] = "You have successfully deposited <color=#66FF00>${0}</color> into account #{1}.",

                ["Account_Withdraw_Usage"] = "Usage: /bank withdraw Amount# Account#\nIf you recently viewed an account, you may omit Account#.",
                ["Account_Withdraw_Success"] = "You have successfully withdrawn <color=#66FF00>${0}</color> from account #{1}.",

                ["Transfer_Usage"] = "Usage: /bank transfer Amount# Account# Account#",
                ["Transfer_Success"] = "You have successfully transferred <color=#66FF00>${0}</color> from account #{1} to account #{2}.",
                ["Transfer_NoAccess"] = "You do not have access to account #{0}. You must have access to transfer money from an account.",

                ["Stimulus_Usage"] = "Usage: /bank stimulus Amount# Account#",
                ["Stimulus_Warning"] = "You are about to distribute <color=#66FF00>${0}</color> from account #{1} to all citizens who own at least one bank account. "
                                     + "Use '/bank stimulus amt# acct# CONFIRM' to confirm the stimulus.",
                ["Stimulus_Failure"] = "Account #{0} lacks sufficient funds for the proposed stimulus.",
                ["Stimulus_Success"] = "You have given each citizen who owns at least one bank account <color=#66FF00>${0}</color> from account #{1}.",
                ["Stimulus_Received"] = "You have just received a stimulus deposit of <color=#66FF00>${0}</color> into account #{1}.",

                ["Wipe_Warning"] = "YOU ARE ABOUT TO WIPE ALL ACCOUNT DATA. THIS CANNOT BE UNDONE. USE '/bank wipe CONFIRM' TO CONFIRM THE WIPE.",
                ["Wipe_Success"] = "YOU HAVE WIPED ALL ACCOUNT DATA. THIS CANNOT BE UNDONE.",

                ["Freeze_Usage"] = "Usage: /bank freeze Account#",
                ["Freeze_Success"] = "Account #{0} has been frozen. Use '/bank unfreeze Account#' to undo this.",

                ["Unfreeze_Usage"] = "Usage: /bank unfreeze Account#",
                ["Unfreeze_Success"] = "Account #{0} freeze has been removed."
            }, this);
        }

        #endregion Localization
	}
}