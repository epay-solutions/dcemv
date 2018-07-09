/*
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
using DCEMV.EMVProtocol.Contact;
using DCEMV.FormattingUtils;
using DCEMV.ISO7816Protocol;
using System;
using System.Diagnostics;

namespace DCEMV.EMVProtocol.Kernels.K
{
    public static class State_5b_WaitingForGetPinTryCounter
    {
        public static SignalsEnum Execute(
          KernelDatabase database,
          KernelQ qManager,
          CardQ cardQManager,
          Stopwatch sw)
        {
            if (qManager.GetOutputQCount() > 0) //there is a pending request to the terminal
            {
                KernelRequest kernel1Request = qManager.DequeueFromInput(false);
                switch (kernel1Request.KernelTerminalReaderServiceRequestEnum)
                {
                    case KernelTerminalReaderServiceRequestEnum.STOP:
                        return EntryPointSTOP(database, qManager);

                    case KernelTerminalReaderServiceRequestEnum.DET:
                        return EntryPointDET(database, kernel1Request);

                    default:
                        throw new EMVProtocolException("Invalid Kernel1TerminalReaderServiceRequestEnum in State_5b_WaitingForGetPinTryCounter:" + Enum.GetName(typeof(CardInterfaceServiceResponseEnum), kernel1Request.KernelTerminalReaderServiceRequestEnum));
                }
            }
            else
            {
                CardResponse cardResponse = cardQManager.DequeueFromOutput(false);
                switch (cardResponse.CardInterfaceServiceResponseEnum)
                {
                    case CardInterfaceServiceResponseEnum.RA:
                        return EntryPointRA(database, cardResponse, qManager, cardQManager, sw);

                    case CardInterfaceServiceResponseEnum.L1RSP:
                        return EntryPointL1RSP(database, cardResponse, qManager);

                    default:
                        throw new EMVProtocolException("Invalid Kernel1CardinterfaceServiceResponseEnum in State_5b_WaitingForGetPinTryCounter:" + Enum.GetName(typeof(CardInterfaceServiceResponseEnum), cardResponse.CardInterfaceServiceResponseEnum));
                }
            }
        }


        private static SignalsEnum EntryPointRA(KernelDatabase database, CardResponse cardResponse, KernelQ qManager, CardQ cardQManager, Stopwatch sw)
        {
            CARDHOLDER_VERIFICATION_METHOD_CVM_RESULTS_9F34_KRN cvr = new CARDHOLDER_VERIFICATION_METHOD_CVM_RESULTS_9F34_KRN(database);
            
            EMVGetDataResponse response = (EMVGetDataResponse)cardResponse.ApduResponse;
            if (!response.Succeeded)
            {
                cvr.Value.CVMResult = 0x01;//failed
                cvr.UpdateDB();
                return SignalsEnum.WAITING_FOR_CVM_PROCESSING;
            }

            //store the pin try counter value
            database.AddToList(response.GetResponseTag());
            int pinTryCounter = Formatting.ConvertToInt16(database.Get(EMVTagsEnum.PERSONAL_IDENTIFICATION_NUMBER_PIN_TRY_COUNTER_9F17_KRN).Value);

             TERMINAL_VERIFICATION_RESULTS_95_KRN tvr = new TERMINAL_VERIFICATION_RESULTS_95_KRN(database);

            if (cvr.Value.GetCVMPerformed() == CVMCode.EncipheredPINVerificationPerformedByICC ||
                cvr.Value.GetCVMPerformed() == CVMCode.EncipheredPINVerificationPerformedByICCAndSignature_Paper)
            {
                string pin = Formatting.ByteArrayToASCIIString(database.Get(EMVTagsEnum.TRANSACTION_PERSONAL_IDENTIFICATION_NUMBER_PIN_DATA_99_KRN.Tag).Value);
                if (!PinProcessing.VerifyOfflinePin(pin, pinTryCounter, tvr, cvr))
                {
                    return SignalsEnum.WAITING_FOR_CVM_PROCESSING;
                }
                //fire off get challenge command
                EMVGetChallengeRequest request = new EMVGetChallengeRequest();
                cardQManager.EnqueueToInput(new CardRequest(request, CardinterfaceServiceRequestEnum.ADPU));
                return SignalsEnum.WAITING_FOR_GET_CHALLENGE;
            }

            if (cvr.Value.GetCVMPerformed() == CVMCode.PlaintextPINVerificationPerformedByICC ||
                cvr.Value.GetCVMPerformed() == CVMCode.PlaintextPINVerificationPerformedByICCAndSignature_Paper)
            {
                string pin = Formatting.ByteArrayToASCIIString(database.Get(EMVTagsEnum.TRANSACTION_PERSONAL_IDENTIFICATION_NUMBER_PIN_DATA_99_KRN.Tag).Value);
                if (!PinProcessing.VerifyOfflinePin(pin, pinTryCounter, tvr, cvr))
                {
                    return SignalsEnum.WAITING_FOR_CVM_PROCESSING;
                }

                byte[] pinData = PinProcessing.BuildPlainTextPinBlock(pin);

                //fire off verify for enciphered pin
                EMVVerifyRequest request = new EMVVerifyRequest(VerifyCommandDataQualifier.Plaintext_PIN, pinData);
                cardQManager.EnqueueToInput(new CardRequest(request, CardinterfaceServiceRequestEnum.ADPU));

                return SignalsEnum.WAITING_FOR_VERIFY;
            }

            throw new EMVProtocolException("Invalid cvr in State_5b_WaitingForGetPinTryCounter");
        }
        private static SignalsEnum EntryPointDET(KernelDatabase database, KernelRequest kernel1Request)
        {
            database.UpdateWithDETData(kernel1Request.InputData);
            return SignalsEnum.WAITING_FOR_GET_PIN_TRY_COUNTER;
        }

        private static SignalsEnum EntryPointL1RSP(KernelDatabase database, CardResponse cardResponse, KernelQ qManager)
        {
            CommonRoutines.InitializeDiscretionaryData(database);
            return CommonRoutines.PostOutcome(database, qManager,
                KernelMessageidentifierEnum.TRY_AGAIN,
                KernelStatusEnum.READY_TO_READ,
                new byte[] { 0x00, 0x00, 0x00 },
                Kernel2OutcomeStatusEnum.END_APPLICATION,
                Kernel2StartEnum.B,
                false,
                KernelMessageidentifierEnum.TRY_AGAIN,
                L1Enum.NOT_SET,
                cardResponse.ApduResponse.SW12,
                L2Enum.STATUS_BYTES,
                L3Enum.NOT_SET);
        }

        private static SignalsEnum EntryPointSTOP(KernelDatabase database, KernelQ qManager)
        {
            CommonRoutines.CreateEMVDiscretionaryData(database);
            return CommonRoutines.PostOutcomeWithError(database, qManager, Kernel2OutcomeStatusEnum.END_APPLICATION, Kernel2StartEnum.N_A, L1Enum.NOT_SET, L2Enum.NOT_SET, L3Enum.STOP);
        }

     
    }
}
