﻿
namespace SV.UPnPLite.Protocols.UPnP
{
    using SV.UPnPLite.Extensions;
    using SV.UPnPLite.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    /// <summary>
    ///     The base class for all UPnP device's services.
    /// </summary>
    public class UPnPService
    {
        #region Fields

        protected readonly ILogger logger;

        private readonly Uri controlUri;

        private readonly Uri eventsUri;

        #endregion

        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="UPnPService" /> class.
        /// </summary>
        /// <param name="serviceType">
        ///     A type of the service.
        /// </param>
        /// <param name="controlUri">
        ///     An URL for sending commands to the service.
        /// </param>
        /// <param name="eventsUri">
        ///     An URL for subscrinbing to service's events.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="serviceType"/> is <c>null</c> or <see cref="string.Empty"/> -OR-
        ///     <paramref name="controlUri"/> is <c>null</c> -OR-
        ///     <paramref name="eventsUri"/> is <c>null</c>.
        /// </exception>
        public UPnPService(string serviceType, Uri controlUri, Uri eventsUri)
        {
            this.ServiceType = serviceType;
            this.controlUri = controlUri;
            this.eventsUri = eventsUri;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UPnPService" /> class.
        /// </summary>
        /// <param name="serviceType">
        ///     A type of the service.
        /// </param>
        /// <param name="controlUri">
        ///     An URL for sending commands to the service.
        /// </param>
        /// <param name="eventsUri">
        ///     An URL for subscrinbing to service's events.
        /// </param>
        /// <param name="logManager">
        ///     The <see cref="ILogManager"/> to use for logging the debug information.
        /// </param>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="serviceType"/> is <c>null</c> or <see cref="string.Empty"/> -OR-
        ///     <paramref name="controlUri"/> is <c>null</c> -OR-
        ///     <paramref name="eventsUri"/> is <c>null</c>.
        /// </exception>
        public UPnPService(string serviceType, Uri controlUri, Uri eventsUri, ILogManager logManager)
            : this(serviceType, controlUri, eventsUri)
        {
            if (logManager != null)
            {
                this.logger = logManager.GetLogger(this.GetType());
            }
        }

        #endregion

        #region Properties

        /// <summary>
        ///     Gets a type of the service.
        /// </summary>
        public string ServiceType { get; internal set; }

        #endregion

        #region Methods

        /// <summary>
        ///     Invokes an <paramref name="action"/> at the device's service.
        /// </summary>
        /// <param name="action">
        ///     An action to invoke.
        /// </param>
        /// <returns>
        ///     A dictionary with result of the action.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     An <see cref="action"/> is <c>null</c> or empty.
        /// </exception>
        /// <exception cref="WebException">
        ///     An error occurred when sending request to service.
        /// </exception>
        /// <exception cref="UPnPServiceException">
        ///     An internal service error occurred when executing request.
        /// </exception>
        public async Task<Dictionary<string, string>> InvokeActionAsync(string action)
        {
            return await this.InvokeActionAsync(action, null);
        }

        /// <summary>
        ///     Invokes an <paramref name="action"/> at the device's service.
        /// </summary>
        /// <param name="action">
        ///     An action to invoke.
        /// </param>
        /// <param name="parameters">
        ///     Invocation parameters.
        /// </param>
        /// <returns>
        ///     A dictionary with result of the action.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     An <see cref="action"/> is <c>null</c> or empty.
        /// </exception>
        /// <exception cref="WebException">
        ///     An error occurred when sending request to service.
        /// </exception>
        /// <exception cref="UPnPServiceException">
        ///     An internal service error occurred when executing request.
        /// </exception>
        public async Task<Dictionary<string, string>> InvokeActionAsync(string action, Dictionary<string, object> parameters)
        {
            var requestXml = this.CreateActionRequest(action, parameters);
            var data = Encoding.UTF8.GetBytes(requestXml);

            var request = WebRequest.Create(this.controlUri);
            request.Method = "POST";
            request.ContentType = "text/xml; charset=\"utf-8\"";
            request.Headers["SOAPACTION"] = "\"{0}#{1}\"".F(this.ServiceType, action);

            using (var stream = await request.GetRequestStreamAsync())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                var response = await request.GetResponseAsync();
                var responseStream = response.GetResponseStream();
                var document = XDocument.Load(responseStream);

                var result = this.ParseActionResponse(action, document);

                return result;
            }
            catch (WebException ex)
            {
                if (ex.Response != null)
                {
                    var responseStream = ex.Response.GetResponseStream();
                    if (responseStream != null)
                    {
                        var document = XDocument.Load(responseStream);
                        var error = this.ParseActionError(document, ex);
                        if (error != null)
                        {
                            error.Action = action;
                            error.Arguments = parameters;

                            throw error;
                        }
                        else
                        {
                            this.logger.Warning("Can't parse an error response: [action={0}]: \n{1}", action, document.ToString());

                            throw;
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }
        }

        private string CreateActionRequest(string action, Dictionary<string, object> parameters)
        {
            var u = XNamespace.Get(this.ServiceType);
            var encodingStyle = XNamespace.Get("http://schemas.xmlsoap.org/soap/encoding/");

            var actionElement = new XElement(u + action, new XAttribute(XNamespace.Xmlns + "u", u.NamespaceName));

            if (parameters != null && parameters.Any())
            {
                foreach (var parameter in parameters)
                {
                    actionElement.Add(new XElement(parameter.Key, parameter.Value));
                }
            }

            var envelope = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(Namespaces.Envelope + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "s", Namespaces.Envelope.NamespaceName),
                    new XAttribute(Namespaces.Envelope + "encodingStyle", encodingStyle.NamespaceName),
                    new XElement(Namespaces.Envelope + "Body", actionElement)));

            var stEnvelope = envelope.ToStringWithDeclaration();
            return stEnvelope;
        }

        private Dictionary<string, string> ParseActionResponse(string action, XDocument response)
        {
            var u = XNamespace.Get(this.ServiceType);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var responseNode = response.Descendants(u + "{0}Response".F(action)).First();

            foreach (var argumentElement in responseNode.Descendants())
            {
                result[argumentElement.Name.LocalName] = argumentElement.Value;
            }

            return result;
        }

        private UPnPServiceException ParseActionError(XDocument error, Exception actualException)
        {
            UPnPServiceException exception = null;

            var upnpErrorElement = error.Descendants(Namespaces.Control + "UPnPError").FirstOrDefault();
            if (upnpErrorElement != null)
            {
                var errorCodeElement = upnpErrorElement.Element(Namespaces.Control + "errorCode");
                var errorDesctiptionElement = upnpErrorElement.Element(Namespaces.Control + "errorDescription");

                int errorCode;
                if (errorCodeElement != null && int.TryParse(errorCodeElement.ValueOrDefault(), out errorCode))
                {
                    exception = new UPnPServiceException(errorCode, errorDesctiptionElement.ValueOrDefault(), actualException);
                }
            }

            return exception;
        }

        #endregion

        #region Types

        /// <summary>
        ///     Defines some standard XML namespaces.
        /// </summary>
        protected static class Namespaces
        {
            public static XNamespace Control = XNamespace.Get("urn:schemas-upnp-org:control-1-0");

            public static XNamespace Envelope = XNamespace.Get("http://schemas.xmlsoap.org/soap/envelope/");
        }

        #endregion
    }
}
