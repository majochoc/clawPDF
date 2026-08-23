using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace BaySenseSystems.BaySenseEmail
{
    public static class EmailComposer
    {
        /// <summary>
        /// Replace {placeholders} in a template with values from DocumentData and Config.
        /// </summary>
        public static string Render(string template, DocumentData data, Config config)
        {
            return template
                .Replace("{ro_number}", data.RONumber)
                .Replace("{document_type}", data.DocumentType)
                .Replace("{document_type_lower}", data.DocumentType.ToLower())
                .Replace("{customer_first_name}", data.CustomerFirstName)
                .Replace("{customer_last_name}", data.CustomerLastName)
                .Replace("{customer_email}", data.CustomerEmail)
                .Replace("{vehicle}", data.Vehicle)
                .Replace("{total}", data.Total)
                .Replace("{shop_name}", config.ShopName)
                .Replace("{shop_phone}", config.ShopPhone);
        }

        /// <summary>
        /// Open the default MAPI email client (Thunderbird) with pre-filled fields.
        /// Uses MAPI32.DLL MAPISendMail — the same approach clawPDF uses in clawPDF.Mail.
        /// </summary>
        public static void OpenMailCompose(string to, string subject, string body,
            string attachmentPath)
        {
            // MAPI must run on an STA thread
            var t = new Thread(() =>
            {
                SendMapi(to, subject, body, attachmentPath);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }

        private static void SendMapi(string to, string subject, string body, string attachmentPath)
        {
            var message = new MapiMessage
            {
                Subject = subject,
                NoteText = body
            };

            // Set up recipient
            IntPtr recipientPtr = IntPtr.Zero;
            if (!string.IsNullOrEmpty(to))
            {
                var recipient = new MapiRecipDesc
                {
                    RecipientClass = 1, // MAPI_TO
                    Name = to,
                    Address = "SMTP:" + to
                };
                recipientPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiRecipDesc)));
                Marshal.StructureToPtr(recipient, recipientPtr, false);
                message.RecipientCount = 1;
                message.Recipients = recipientPtr;
            }

            // Set up attachment
            IntPtr filePtr = IntPtr.Zero;
            if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
            {
                var fullPath = Path.GetFullPath(attachmentPath);
                var pathPtr = Marshal.AllocHGlobal(260 * Marshal.SystemMaxDBCSCharSize);
                RtlMoveStringAnsi(pathPtr, fullPath, (IntPtr)(fullPath.Length + 1));

                var fileDesc = new MapiFileDescriptor
                {
                    position = -1,
                    name = Path.GetFileName(fullPath),
                    path = pathPtr
                };
                filePtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDescriptor)));
                Marshal.StructureToPtr(fileDesc, filePtr, false);
                message.FileCount = 1;
                message.Files = filePtr;
            }

            const int MAPI_DIALOG = 0x8;
            int result = MAPISendMail(IntPtr.Zero, IntPtr.Zero, message, MAPI_DIALOG, 0);

            // Clean up
            if (filePtr != IntPtr.Zero)
            {
                var fileDesc = (MapiFileDescriptor)Marshal.PtrToStructure(filePtr, typeof(MapiFileDescriptor));
                if (fileDesc.path != IntPtr.Zero)
                    Marshal.FreeHGlobal(fileDesc.path);
                Marshal.DestroyStructure(filePtr, typeof(MapiFileDescriptor));
                Marshal.FreeHGlobal(filePtr);
            }
            if (recipientPtr != IntPtr.Zero)
            {
                Marshal.DestroyStructure(recipientPtr, typeof(MapiRecipDesc));
                Marshal.FreeHGlobal(recipientPtr);
            }

            if (result != 0 && result != 1) // 1 = user abort, which is fine
                throw new Exception($"MAPISendMail failed with error code {result}");
        }

        #region MAPI Interop

        [DllImport("MAPI32.DLL")]
        private static extern int MAPISendMail(IntPtr session, IntPtr hwnd,
            MapiMessage message, int flg, int rsv);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", CharSet = CharSet.Ansi)]
        private static extern void RtlMoveStringAnsi(IntPtr pdst, string psrc, IntPtr sizetcb);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class MapiMessage
        {
            public int Reserved = 0;
            public string Subject;
            public string NoteText;
            public string MessageType = null;
            public string DateReceived = null;
            public string ConversationID = null;
            public int Flags = 0;
            public IntPtr Originator = IntPtr.Zero;
            public int RecipientCount = 0;
            public IntPtr Recipients = IntPtr.Zero;
            public int FileCount = 0;
            public IntPtr Files = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class MapiRecipDesc
        {
            public int Reserved = 0;
            public int RecipientClass;
            public string Name;
            public string Address;
            public int eIDSize = 0;
            public IntPtr EntryID = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private class MapiFileDescriptor
        {
            public int reserved = 0;
            public int flags = 0;
            public int position;
            public IntPtr path;
            public string name;
            public IntPtr type = IntPtr.Zero;
        }

        #endregion
    }
}
