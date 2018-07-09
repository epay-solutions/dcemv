﻿/*
*************************************************************************
DC EMV
Open Source EMV
Copyright (C) 2018  Vicente Da Silva

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see http://www.gnu.org/licenses/
*************************************************************************
*/
using DCEMV.Shared;
using DCEMV.EMVProtocol;
using DCEMV.EMVProtocol.Kernels;
using DCEMV.ISO7816Protocol;
using System;
using System.Threading.Tasks;
using DCEMV.TLVProtocol;
using Xamarin.Forms;
using DCEMV.ServerShared;
using DCEMV.FormattingUtils;
using DCEMV.TerminalCommon;

namespace DCEMV.DemoApp
{

    public partial class TopUpView : ModalPage
    {
        private TransactionRequest tr;
        private TotalAmountViewModel totalAmount;

        //for EMVTxCtl
        private IConfigurationProvider configProvider;
        private ICardInterfaceManger contactCardInterfaceManger;
        private ICardInterfaceManger contactlessCardInterfaceManger;
        private IOnlineApprover onlineApprover;
        private TCPClientStream tcpClientStream;

        public TopUpView(ICardInterfaceManger contactCardInterfaceManger, ICardInterfaceManger contactlessCardInterfaceManger, IConfigurationProvider configProvider, IOnlineApprover onlineApprover, TCPClientStream tcpClientStream)
        {
            InitializeComponent();

            this.contactCardInterfaceManger = contactCardInterfaceManger;
            this.contactlessCardInterfaceManger = contactlessCardInterfaceManger;
            this.configProvider = configProvider;
            this.onlineApprover = onlineApprover;
            this.tcpClientStream = tcpClientStream;

            emvTxCtl.TxCompleted += EmvTxCtl_TxCompleted;

            totalAmount = new TotalAmountViewModel();
            totalAmount.Total = "100";
            gridProgress.IsVisible = false;
            txtAmount.BindingContext = totalAmount;
            lblStatusAskAmount.Text = "Enter the amount below that you wish to top up with.";
            UpdateView(ViewState.Step1TransactDetails);
        }

        private void cmdNextToCC_Clicked(object sender, EventArgs e)
        {
            emvTxCtl.SetTxStartLabel("Please Tap the Visa or MasterCard card you wish to make the TopUp payment with.");
            UpdateView(ViewState.Step2TapCard);

            long amount = Convert.ToInt64(totalAmount.Total);
            long amountOther = 0;
            tr = new TransactionRequest(amount + amountOther, amountOther, TransactionTypeEnum.PurchaseGoodsAndServices);

            emvTxCtl.Start(tr, contactCardInterfaceManger, SessionSingleton.ContactDeviceId,
                contactlessCardInterfaceManger, SessionSingleton.ContactlessDeviceId,
                configProvider, onlineApprover, tcpClientStream);
        }

        private void UpdateView(ViewState viewState)
        {
            switch (viewState)
            {
                case ViewState.Step1TransactDetails:
                    gridTransactDetails.IsVisible = true;
                    emvTxCtl.IsVisible = false;
                    gridTransactSummary.IsVisible = false;
                    break;

                case ViewState.Step2TapCard:
                    gridTransactDetails.IsVisible = false;
                    emvTxCtl.IsVisible = true;
                    gridTransactSummary.IsVisible = false;
                    break;

                case ViewState.Step3Summary:
                    gridTransactDetails.IsVisible = false;
                    emvTxCtl.IsVisible = false;
                    gridTransactSummary.IsVisible = true;
                    break;
            }
        }

        private async void EmvTxCtl_TxCompleted(object sender, EventArgs e)
        {
            try
            {
                long? amount = Convert.ToInt64(totalAmount.Total);

                if ((e as TxCompletedEventArgs).EMV_Data.IsPresent())
                {
                    if ((e as TxCompletedEventArgs).TxResult == TxResult.Approved ||
                        (e as TxCompletedEventArgs).TxResult == TxResult.ContactlessOnline)
                    {
                        TLV data = (e as TxCompletedEventArgs).EMV_Data.Get();
                        try
                        {
                            await CallTopUpWebService(SessionSingleton.Account.AccountNumberId, amount, "000", data);
                            Device.BeginInvokeOnMainThread(() =>
                            {
                                lblStatusTopUp.Text = "Transaction Completed Succesfully";
                                UpdateView(ViewState.Step3Summary);
                            });
                        }
                        catch
                        {
                            Device.BeginInvokeOnMainThread(() =>
                            {
                                lblStatusTopUp.Text = "Declined, could not go online.";
                                UpdateView(ViewState.Step3Summary);
                            });
                        }
                    }
                    else
                    {
                        Device.BeginInvokeOnMainThread(() =>
                        {
                            lblStatusTopUp.Text = (e as TxCompletedEventArgs).TxResult.ToString();
                            UpdateView(ViewState.Step3Summary);
                        });
                    }
                }
                else
                {
                    Device.BeginInvokeOnMainThread(() =>
                    {
                        lblStatusTopUp.Text = "Declined";
                        UpdateView(ViewState.Step3Summary);
                    });
                }
            }
            catch (Exception ex)
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    lblStatusTopUp.Text = ex.Message;
                    UpdateView(ViewState.Step3Summary);
                });
            }
        }

        private async Task CallTopUpWebService(string toAccountNumber, long? amount, string cvv, TLV emvData)
        {
            Device.BeginInvokeOnMainThread(() =>
            {
                gridProgress.IsVisible = true;
            });
            try
            {
                Proxies.DCEMVDemoServerClient client = SessionSingleton.GenDCEMVServerApiClient();
                using (SessionSingleton.HttpClient)
                {
                    CCTopUpTransaction tx = new CCTopUpTransaction()
                    {
                        Amount = amount.Value,
                        CVV = cvv,
                        EMV_Data = TLVasJSON.ToJSON(emvData),
                    };
                    await client.TransactionTopupPostAsync(tx.ToJsonString());
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                Device.BeginInvokeOnMainThread(() =>
                {
                    gridProgress.IsVisible = false;
                });
            }
        }

        protected override void OnDisappearing()
        {
            emvTxCtl.Stop();

            UpdateView(ViewState.Step1TransactDetails);

            base.OnDisappearing();
        }
    }
}
