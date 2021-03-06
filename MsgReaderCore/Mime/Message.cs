using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Mime;
using MsgReader.Helpers;
using MsgReader.Mime.Header;
using MsgReader.Mime.Traverse;

namespace MsgReader.Mime
{
	/// <summary>
	/// This is the root of the email tree structure.<br/>
	/// <see cref="Mime.MessagePart"/> for a description about the structure.<br/>
	/// <br/>
	/// A Message (this class) contains the headers of an email message such as:
	/// <code>
	///  - To
	///  - From
	///  - Subject
	///  - Content-Type
	///  - Message-ID
	/// </code>
	/// which are located in the <see cref="Headers"/> property.<br/>
	/// <br/>
	/// Use the <see cref="Message.MessagePart"/> property to find the actual content of the email message.
	/// </summary>
	public class Message
	{
		#region Properties
        /// <summary>
        /// Returns the ID of the message when this is available in the <see cref="Headers"/>
        /// (as specified in [RFC2822]). Null when not available
        /// </summary>
        public string Id
        {
            get
            {
                if (Headers == null)
                    return null;

                return !string.IsNullOrEmpty(Headers.MessageId) ? Headers.MessageId : null;
            }
        }

		/// <summary>
		/// Headers of the Message.
		/// </summary>
		public MessageHeader Headers { get; }

		/// <summary>
		/// This is the body of the email Message.<br/>
		/// <br/>
		/// If the body was parsed for this Message, this property will never be <see langword="null"/>.
		/// </summary>
		public MessagePart MessagePart { get; }

        /// <summary>
        /// This will return the first <see cref="MessagePart"/> where the <see cref="ContentType.MediaType"/>
        /// is set to "html/text". This will return <see langword="null"/> when there is no "html/text" 
        /// <see cref="MessagePart"/> found.
        /// </summary>
        public MessagePart HtmlBody { get; }

        /// <summary>
        /// This will return the first <see cref="MessagePart"/> where the <see cref="ContentType.MediaType"/>
        /// is set to "text/plain". This will be <see langword="null"/> when there is no "text/plain" 
        /// <see cref="MessagePart"/> found.
        /// </summary>
        public MessagePart TextBody { get; }

        /// <summary>
        /// This will return all the <see cref="MessagePart">message parts</see> that are flagged as 
        /// <see cref="Mime.MessagePart.IsAttachment"/>.
        /// This will be <see langword="null"/> when there are no <see cref="MessagePart">message parts</see> 
        /// that are flagged as <see cref="Mime.MessagePart.IsAttachment"/>.
        /// </summary>
        public ReadOnlyCollection<MessagePart> Attachments { get; } 

        /// <summary>
		/// The raw content from which this message has been constructed.<br/>
		/// These bytes can be persisted and later used to recreate the Message.
		/// </summary>
		public byte[] RawMessage { get; }
		#endregion

		#region Constructors
		/// <summary>
		/// Convenience constructor for <see cref="Mime.Message(byte[], bool)"/>.<br/>
		/// <br/>
		/// Creates a message from a byte array. The full message including its body is parsed.
		/// </summary>
		/// <param name="rawMessageContent">The byte array which is the message contents to parse</param>
		public Message(byte[] rawMessageContent) : this(rawMessageContent, true) {}

		/// <summary>
		/// Constructs a message from a byte array.<br/>
		/// <br/>
		/// The headers are always parsed, but if <paramref name="parseBody"/> is <see langword="false"/>, the body is not parsed.
		/// </summary>
		/// <param name="rawMessageContent">The byte array which is the message contents to parse</param>
		/// <param name="parseBody">
		/// <see langword="true"/> if the body should be parsed,
		/// <see langword="false"/> if only headers should be parsed out of the <paramref name="rawMessageContent"/> byte array
		/// </param>
		public Message(byte[] rawMessageContent, bool parseBody)
		{
            Logger.WriteToLog("Processing raw EML message content");

			RawMessage = rawMessageContent;

			// Find the headers and the body parts of the byte array
		    HeaderExtractor.ExtractHeadersAndBody(rawMessageContent, out var headersTemp, out var body);

			// Set the Headers property
			Headers = headersTemp;

			// Should we also parse the body?
			if (parseBody)
			{
				// Parse the body into a MessagePart
				MessagePart = new MessagePart(body, Headers);

			    var findBodyMessagePartWithMediaType = new FindBodyMessagePartWithMediaType();

                // Searches for the first HTML body and mark this one as the HTML body of the E-mail
                HtmlBody = findBodyMessagePartWithMediaType.VisitMessage(this, "text/html");
                if (HtmlBody != null)
			        HtmlBody.IsHtmlBody = true;

                // Searches for the first TEXT body and mark this one as the TEXT body of the E-mail
                TextBody = findBodyMessagePartWithMediaType.VisitMessage(this, "text/plain");
                if (TextBody != null)
                    TextBody.IsTextBody = true;

                var attachments = new AttachmentFinder().VisitMessage(this);

			    if (HtmlBody != null)
			    {
			        foreach (var attachment in attachments)
			        {
                        if (attachment.IsInline) continue;
			            var htmlBody = HtmlBody.BodyEncoding.GetString(HtmlBody.Body);
			            attachment.IsInline = htmlBody.Contains($"cid:{attachment.ContentId}");
			        }
			    }

			    if (attachments != null)
			        Attachments = attachments.AsReadOnly();
			}

            Logger.WriteToLog("Raw EML message content processed");
		}
		#endregion

        #region GetEmailAddresses
        /// <summary>
        /// Returns the list of <see cref="RfcMailAddress"/> as a normal or html string
        /// </summary>
        /// <param name="rfcMailAddresses">A list with one or more <see cref="RfcMailAddress"/> objects</param>
        /// <param name="convertToHref">When true the E-mail addresses are converted to hyperlinks</param>
        /// <param name="html">Set this to true when the E-mail body format is html</param>
        /// <returns></returns>
        public string GetEmailAddresses(IEnumerable<RfcMailAddress> rfcMailAddresses, bool convertToHref, bool html)
        {
            Logger.WriteToLog("Getting mail addresses");

            var result = string.Empty;

            if (rfcMailAddresses == null)
                return result;

            foreach (var rfcMailAddress in rfcMailAddresses)
            {
                if (result != string.Empty)
                    result += "; ";

                var emailAddress = string.Empty;
                var displayName = rfcMailAddress.DisplayName;

                if (rfcMailAddress.HasValidMailAddress)
                    emailAddress = rfcMailAddress.Address;

                if (string.Equals(emailAddress, displayName, StringComparison.InvariantCultureIgnoreCase))
                    displayName = string.Empty;

                if (html)
                {
                    emailAddress = WebUtility.HtmlEncode(emailAddress);
                    displayName = WebUtility.HtmlEncode(displayName);
                }

                if (convertToHref && html && !string.IsNullOrEmpty(emailAddress))
                    result += "<a href=\"mailto:" + emailAddress + "\">" +
                              (!string.IsNullOrEmpty(displayName)
                                  ? displayName
                                  : emailAddress) + "</a>";

                else
                {
                    if (!string.IsNullOrEmpty(displayName))
                        result += displayName;

                    var beginTag = string.Empty;
                    var endTag = string.Empty;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        if (html)
                        {
                            beginTag = "&nbsp;&lt;";
                            endTag = "&gt;";
                        }
                        else
                        {
                            beginTag = " <";
                            endTag = ">";
                        }
                    }

                    if (!string.IsNullOrEmpty(emailAddress))
                        result += beginTag + emailAddress + endTag;
                }
            }

            return result;
        }
        #endregion

        #region Save
		/// <summary>
		/// Save this <see cref="Message"/> to a file.<br/>
		/// <br/>
		/// Can be loaded at a later time using the <see cref="Load(FileInfo)"/> method.
		/// </summary>
		/// <param name="file">The File location to save the <see cref="Message"/> to. Existent files will be overwritten.</param>
		/// <exception cref="ArgumentNullException">If <paramref name="file"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to using a <see cref="FileStream"/> might be thrown as well</exception>
		// ReSharper disable once UnusedMember.Global
		public void Save(FileInfo file)
		{
			if (file == null)
				throw new ArgumentNullException(nameof(file));

			using (var fileStream = new FileStream(file.FullName, FileMode.Create))
				Save(fileStream);
		}

		/// <summary>
		/// Save this <see cref="Message"/> to a stream.<br/>
		/// </summary>
		/// <param name="messageStream">The stream to write to</param>
		/// <exception cref="ArgumentNullException">If <paramref name="messageStream"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to <see cref="Stream.Write"/> might be thrown as well</exception>
		public void Save(Stream messageStream)
		{
			if (messageStream == null)
				throw new ArgumentNullException(nameof(messageStream));

			messageStream.Write(RawMessage, 0, RawMessage.Length);
		}
        #endregion

        #region Load
        /// <summary>
		/// Loads a <see cref="Message"/> from a file containing a raw email.
		/// </summary>
		/// <param name="file">The File location to load the <see cref="Message"/> from. The file must exist.</param>
		/// <exception cref="ArgumentNullException">If <paramref name="file"/> is <see langword="null"/></exception>
		/// <exception cref="FileNotFoundException">If <paramref name="file"/> does not exist</exception>
		/// <exception>Other exceptions relevant to a <see cref="FileStream"/> might be thrown as well</exception>
		/// <returns>A <see cref="Message"/> with the content loaded from the <paramref name="file"/></returns>
		public static Message Load(FileInfo file)
		{
            Logger.WriteToLog($"Loading EML file from '{file.FullName}'");

			if (file == null)
				throw new ArgumentNullException(nameof(file));

			if (!file.Exists)
				throw new FileNotFoundException("Cannot load message from non-existent file", file.FullName);

			using (var fileStream = new FileStream(file.FullName, FileMode.Open))
                return Load(fileStream);
		}

		/// <summary>
		/// Loads a <see cref="Message"/> from a <see cref="Stream"/> containing a raw email.
		/// </summary>
		/// <param name="messageStream">The <see cref="Stream"/> from which to load the raw <see cref="Message"/></param>
		/// <exception cref="ArgumentNullException">If <paramref name="messageStream"/> is <see langword="null"/></exception>
		/// <exception>Other exceptions relevant to <see cref="Stream.Read"/> might be thrown as well</exception>
		/// <returns>A <see cref="Message"/> with the content loaded from the <paramref name="messageStream"/></returns>
		public static Message Load(Stream messageStream)
		{
		    Logger.WriteToLog("Loading EML file from stream");

			if (messageStream == null)
				throw new ArgumentNullException(nameof(messageStream));

			using (var memoryStream = new MemoryStream())
			{
                messageStream.CopyTo(memoryStream);
                var content = memoryStream.ToArray();
                return new Message(content);
			}
		}
		#endregion
	}
}
