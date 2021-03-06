﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace Microsoft.Azure.Devices.Client.Transport.AmqpIoT
{
    internal class AmqpIoTSendingLink
    {
        public event EventHandler Closed;
        private SendingAmqpLink _sendingAmqpLink;

        public AmqpIoTSendingLink(SendingAmqpLink sendingAmqpLink)
        {
            _sendingAmqpLink = sendingAmqpLink;
            _sendingAmqpLink.Closed += _sendingAmqpLinkClosed;
        }

        private void _sendingAmqpLinkClosed(object sender, EventArgs e)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(_sendingAmqpLinkClosed)}");
            Closed.Invoke(sender, e);
        }

        internal Task CloseAsync(TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(CloseAsync)}");
            return _sendingAmqpLink.CloseAsync(timeout);
        }

        internal void Abort()
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(Abort)}");
            _sendingAmqpLink.Abort();
        }

        #region Telemetry handling
        internal async Task<AmqpIoTOutcome> SendMessageAsync(Message message, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, message, $"{nameof(SendMessageAsync)}");

            AmqpMessage amqpMessage = AmqpIoTMessageConverter.MessageToAmqpMessage(message);
            Outcome outcome = await SendAmqpMessageAsync(amqpMessage, timeout).ConfigureAwait(false);

            if (Logging.IsEnabled) Logging.Exit(this, message, $"{nameof(SendMessageAsync)}");

            return new AmqpIoTOutcome(outcome);
        }

        internal async Task<AmqpIoTOutcome> SendMessagesAsync(IEnumerable<Message> messages, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(SendMessagesAsync)}");

            // List to hold messages in Amqp friendly format
            var messageList = new List<Data>();

            foreach (Message message in messages)
            {
                using (AmqpMessage amqpMessage = AmqpIoTMessageConverter.MessageToAmqpMessage(message))
                {
                    var data = new Data()
                    {
                        Value = amqpMessage.DataBody
                    };
                    messageList.Add(data);
            }
            }

            Outcome outcome;
            using (AmqpMessage amqpMessage = AmqpMessage.Create(messageList))
            {
                amqpMessage.MessageFormat = AmqpConstants.AmqpBatchedMessageFormat;
                outcome = await SendAmqpMessageAsync(amqpMessage, timeout).ConfigureAwait(false);
            }

            AmqpIoTOutcome amqpIoTOutcome = new AmqpIoTOutcome(outcome);
            if (amqpIoTOutcome != null)
            {
                amqpIoTOutcome.ThrowIfNotAccepted();
            }

            if (Logging.IsEnabled) Logging.Exit(this, $"{nameof(SendMessagesAsync)}");

            return amqpIoTOutcome;
        }

        private async Task<Outcome> SendAmqpMessageAsync(AmqpMessage amqpMessage, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(SendAmqpMessageAsync)}");

            return await _sendingAmqpLink.SendMessageAsync(
                amqpMessage,
                new ArraySegment<byte>(Guid.NewGuid().ToByteArray()),
                AmqpConstants.NullBinary,
                timeout).ConfigureAwait(false);
        }
        #endregion

        #region Method handling
        internal async Task<AmqpIoTOutcome> SendMethodResponseAsync(MethodResponseInternal methodResponse, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, methodResponse, $"{nameof(SendMethodResponseAsync)}");

            AmqpMessage amqpMessage = AmqpIoTMessageConverter.ConvertMethodResponseInternalToAmqpMessage(methodResponse);
            AmqpIoTMessageConverter.PopulateAmqpMessageFromMethodResponse(amqpMessage, methodResponse);

            Outcome outcome = await SendAmqpMessageAsync(amqpMessage, timeout).ConfigureAwait(false);

            if (Logging.IsEnabled) Logging.Exit(this, $"{nameof(SendMethodResponseAsync)}");

            return new AmqpIoTOutcome(outcome);
        }
        #endregion

        #region Twin handling
        internal async Task<AmqpIoTOutcome> SendTwinGetMessageAsync(string correlationId, TwinCollection reportedProperties, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(SendTwinGetMessageAsync)}");

            AmqpMessage amqpMessage = AmqpMessage.Create();
            amqpMessage.Properties.CorrelationId = correlationId;
            amqpMessage.MessageAnnotations.Map["operation"] = "GET";

            Outcome outcome = await SendAmqpMessageAsync(amqpMessage, timeout).ConfigureAwait(false);

            if (Logging.IsEnabled) Logging.Exit(this, $"{nameof(SendTwinGetMessageAsync)}");

            return new AmqpIoTOutcome(outcome);
        }

        internal async Task<AmqpIoTOutcome> SendTwinPatchMessageAsync(string correlationId, TwinCollection reportedProperties, TimeSpan timeout)
        {
            if (Logging.IsEnabled) Logging.Enter(this, $"{nameof(SendTwinPatchMessageAsync)}");

            var body = JsonConvert.SerializeObject(reportedProperties);
            var bodyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

            AmqpMessage amqpMessage = AmqpMessage.Create(bodyStream, true);
            amqpMessage.Properties.CorrelationId = correlationId;
            amqpMessage.MessageAnnotations.Map["operation"] = "PATCH";
            amqpMessage.MessageAnnotations.Map["resource"] = "/properties/reported";
            amqpMessage.MessageAnnotations.Map["version"] = null;

            Outcome outcome = await SendAmqpMessageAsync(amqpMessage, timeout).ConfigureAwait(false);

            if (Logging.IsEnabled) Logging.Exit(this, $"{nameof(SendTwinPatchMessageAsync)}");

            return new AmqpIoTOutcome(outcome);
        }
        #endregion
    }
}
