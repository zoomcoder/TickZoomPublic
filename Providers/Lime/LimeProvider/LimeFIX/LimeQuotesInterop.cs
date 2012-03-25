using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Net;
using TickZoom.Api;

namespace TickZoom.LimeQuotes
{
    // Code translated from C++ Citrus API supplied by Lime.
    internal static class LimeQuotesInterop
    {
        //from quoteSystemApi.cc
        public const int majorVersion = 2;
        public const int minorVersion = 0;
        public const int heartbeat = 30;
        public const int heartbeatTimeout = 90;

        // From limeq_def.h
        // Common constants.
        public const int HEADER_BYTES = 3;
        public const char PAD_CHAR = ' ';
        public const char NUM_PAD_CHAR = '0';

        public const int LIMEQ_MAJOR_VER = 2;
        public const int LIMEQ_MINOR_VER = 0;
        public const int QUOTE_SERVER_PORT = 7047;
        public const int MAX_SUBSCRIPTION_REQUEST = 16 * 1024;

        public const int SYMBOL_LEN = 21;
        public const int LIME_SYMBOL_LEN = 8;

        public enum limeq_message_type : byte
        {
            LOGIN_REQUEST = (byte)'L',
            LOGIN_RESPONSE = (byte)'E',
            LOGOUT_REQUEST = (byte)'O',
            HEARTBEAT = (byte)'H',
            SUBSCRIPTION_REQUEST = (byte)'S',
            SUBSCRIPTION_REPLY = (byte)'P',
            BOOK_REBUILD = (byte)'R',
            ORDER = (byte)'A',
            TRADE = (byte)'T',
            QUOTE_SOURCE_CONTROL = (byte)'M',
            LIMEQ_CONTROL = (byte)'Q',
            TRADING_ACTION = (byte)'X',
            MOD_EXECUTION = (byte)'W',
            ORDER_REPLACE = (byte)'D',
            CLI_MESSAGE = (byte)'K',
            IMBALANCE_MESSAGE = (byte)'I',
            OPTION_ATTRIBUTES = (byte)'B',
            SECURITY_DEFINITION = (byte)'Y',
            TRADE_CANCEL_CORRECT = (byte)'Z',
            SYMBOL_STATUS = (byte)'N',
            FEATURE_CONTROL = (byte)'F',
            FEATURE_CONTROL_RESPONSE = (byte)'G',
            INDEX = (byte)'J',
            SPREAD_DEFINITION = (byte)'C',
            OPTION_ANALYTICS = (byte)'U',
            OPTION_SHOCKED_PNL = (byte)'V',

            // Lower case values are reserved for message types internal
            // to Citrius, used in the quote request queue.
            SYMBOL_ID_MAPPING = (byte)'a',
            QBM_CONTROL_MSG = (byte)'b',
            CLEAR_BOOK_MESSAGE = (byte)'c',
            CLEAR_BOOK_ITER = (byte)'d',
            CLEAR_BOOK_BY_SYMBOL_RANGE_MESSAGE = (byte)'e',
            SUMMARY_DATA = (byte)'f',
            SWEEP_THROUGH_BOOK_MESSAGE = (byte)'s',
            SYMBOL_INIT = (byte)'i',
            BOVESPA_THEORETICAL_PRICE_UPDATE = (byte)'o',
            BOVESPA_THEORETICAL_PRICE_LIST_CLEAR = (byte)'p',
            BOOK_DIVIDER = (byte)'e',
            MESSAGE_NONE = (byte)'n',
            REBUILD_VENUE_HASH = (byte)'h',
            REBUILD_SYMBOL_HASH = (byte)'y',
            TRADE_WITH_BROKER_IDS = (byte)'t',

            // Optional field messages
            OPTIONAL_FIELDS_BOUNDARY = (byte)200,
            OPTIONAL_EXCHANGE_ORDER_ID = (byte)201,
            OPTIONAL_EXCHANGE_ORDER_IDS_ORDER_REPLACE = (byte)202,
            OPTIONAL_BROKER_IDS = (byte)203,
            OPTIONAL_USEC_TIMESTAMP = (byte)204,
            OPTIONAL_USEC_TIMESTAMP_ORDER_REPLACE = (byte)205
        }

        public enum limeq_control_code : byte
        {
            SLOW_MESSAGE_PROCESSING = (byte) 'S',
            MESSAGE_DROPPED = (byte) 'D',
            START_OF_SESSION = (byte) 'B',
            END_OF_SESSION = (byte) 'E',
            QUOTE_SOURCE_CONNECTED = (byte) 'C',
            QUOTE_SOURCE_DISCONNECTED = (byte) 'X',
            EXTENDED_LIMEQ_CONTROL_MSG =(byte) 'T'
            //QUOTE_SOURCE_REWIND_START = 'R',
            //QUOTE_SOURCE_REWIND_END   = 'W'
        }

        public enum limeq_control_subtype
        {
            IO_STATS = 'I',
            MSG_COUNT = 'M'
        }

        public enum trading_action_code
        {
            REG_SHO_UPDATE = '0',
            OPENING_DELAY = '1',
            TRADING_HALT = '2',
            TRADING_RESUME = '3',
            NO_OPEN_NO_RESUME = '4',
            PRICE_INDICATION = '5',
            TRADING_RANGE_INDICATION = '6',
            MARKET_IMBALANCE_BUY = '7',
            MARKET_IMBALANCE_SELL = '8',
            MARKET_ON_CLOSE_IMBALALNCE_BUY = '9',
            MARKET_ON_CLOSE_IMBALALNCE_SELL = 'A',
            NO_MARKET_IMBALANCE = 'C',
            NO_MARKET_ON_CLOSE_IMBALANCE = 'D',
            QUOTE_RESUME = 'Q',
            EXCHANGE_TRADING_HALT = 'V',
            EXCHANGE_QUOTE_RESUME = 'R'
        }

        public enum short_sale_restriction_code
        {
            SHORT_SALE_RESTRICTION_ACTIVATED = 'a',
            SHORT_SALE_RESTRICTION_IN_EFFECT = 'e',
            SHORT_SALE_RESTRICTION_CONTINUED = 'c',
            SHORT_SALE_RESTRICTION_DEACTIVATED = 'd',
            SHORT_SALE_RESTRICTION_NOT_IN_EFFECT = 'n',
            TRADING_STATUS_UPDATE = 't'
        }

        public enum limeq_feature_index
        {
            FEATURE_INDEX_TRADE_REPLAY,
            FEATURE_INDEX_TRADES_ONLY,
            FEATURE_INDEX_TOP_OF_BOOK,
            FEATURE_INDEX_PRICE_AGGREGATED,
            FEATURE_INDEX_FILTER_MMID,
            FEATURE_INDEX_SNAPSHOT_BOOK,
            FEATURE_INDEX_ORDER_VISIBILITY,
            FEATURE_INDEX_BROKER_IDS,
            FEATURE_INDEX_USEC_TIMESTAMP,
            FEATURE_INDEX_MAX,
            FEATURE_INDEX_RATE_LIMIT // supported only by Options Analytics feed
        }

        public enum limeq_feature_code
        {
            FEATURE_TRADE_REPLAY = 1 << limeq_feature_index.FEATURE_INDEX_TRADE_REPLAY,
            FEATURE_TRADES_ONLY = 1 << limeq_feature_index.FEATURE_INDEX_TRADES_ONLY,
            FEATURE_TOP_OF_BOOK = 1 << limeq_feature_index.FEATURE_INDEX_TOP_OF_BOOK,
            FEATURE_PRICE_AGGREGATED = 1 << limeq_feature_index.FEATURE_INDEX_PRICE_AGGREGATED,
            FEATURE_FILTER_MMID = 1 << limeq_feature_index.FEATURE_INDEX_FILTER_MMID,
            FEATURE_SNAPSHOT_BOOK = 1 << limeq_feature_index.FEATURE_INDEX_SNAPSHOT_BOOK,
            FEATURE_ORDER_VISIBILITY = 1 << limeq_feature_index.FEATURE_INDEX_ORDER_VISIBILITY,
            FEATURE_BROKER_IDS = 1 << limeq_feature_index.FEATURE_INDEX_BROKER_IDS,
            FEATURE_USEC_TIMESTAMP = 1 << limeq_feature_index.FEATURE_INDEX_USEC_TIMESTAMP,
            FEATURE_RATE_LIMIT = 1 << limeq_feature_index.FEATURE_INDEX_RATE_LIMIT
        }

        public const int MSG_LEN_SIZE = 2;

        public const int HEADER_LEN = (sizeof(Int16) + sizeof(Byte));

        public enum app_type : byte
        {
            GUI = (byte) 'G',
            JAVA_API = (byte)'J',
            CPP_API = (byte)'N',
            RAW_SOCKET = (byte)'R',
            LIMEQ_REPEATER = (byte)'L',
            CLI_SESSION = (byte)'C'
        }

        public const int UNAME_LEN = 32;
        public const int PASSWD_LEN = 32;

        public enum auth_types : byte
        {
            CLEAR_TEXT = 0
        }

        public const int AUTH_TYPE_POS = 0;
        public const int AUTH_TYPE_LEN = 2;
        public const int HOST_ID_LEN = 6;

        [StructLayout(LayoutKind.Explicit, Pack = 0, Size = 80)]
        public unsafe struct login_request_msg
        {

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public ushort msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public auth_types auth_type;

            [MarshalAs(UnmanagedType.I1, SizeConst = 32)]
            [FieldOffset(4)]
            public fixed byte uname[UNAME_LEN];

            [MarshalAs(UnmanagedType.I1, SizeConst = 32)]
            [FieldOffset(36)]
            public fixed byte passwd[PASSWD_LEN];

            [MarshalAs(UnmanagedType.I1, SizeConst = 6)]
            [FieldOffset(68)]
            public fixed byte host_id[HOST_ID_LEN];

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(74)]
            public byte reserved; /* was "login_flags" */

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(75)]
            public app_type session_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(76)]
            public byte heartbeat_interval;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(77)]
            public byte timeout_interval;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(78)]
            public byte ver_major;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(79)]
            public byte ver_minor;                              //[FieldOffset(79)]
            // Length 80

#if NOTUSED
            public unsafe byte[] ToBytes()
            {
                msg_len = 80;
                MemoryStream stream = new MemoryStream(80);
                var gch = GCHandle.Alloc(this, GCHandleType.Pinned);
                var stream2 = new UnmanagedMemoryStream(gch.AddrOfPinnedObject,sizeof(login_request_msg));
                gch.Free();

                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.Write(msg_len);
                    writer.Write((byte) msg_type);
                    writer.Write(auth_type);
                    writer.Write(StringToBytes(uname, UNAME_LEN));
                    writer.Write(StringToBytes(passwd, PASSWD_LEN));
                    writer.Write(StringToBytes(host_id, HOST_ID_LEN));
                    writer.Write(reserved);
                    writer.Write(session_type);
                    writer.Write(heartbeat_interval);
                    writer.Write(timeout_interval);
                    writer.Write(ver_major);
                    writer.Write(ver_minor);
                }
                return stream.ToArray();
            } 
#endif


        }

        public enum reject_reason_code : byte
        {
            INVALID_UNAME_PASSWORD = (byte) 'A',
            VERSION_MISMATCH = (byte)'V',
            REWIND = (byte)'R',
            ADMIN_DISABLED = (byte)'D',
            ACCOUNT_DISABLED = (byte)'T',
            INSUFFICIENT_PRIVILEGE = (byte)'I',
            INVALID_IP = (byte)'N',
            LOGIN_FAILED = (byte)'F',
            EXCEEDED_MAX_SESSIONS = (byte)'M',
            LOGIN_SUCCEEDED = (byte)'S',
            INVALID_SERVER_GROUP = (byte)'G'
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct login_response_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public ushort msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public byte ver_major;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(4)]
            public byte ver_minor;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(5)]
            public byte heartbeat_interval;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(6)]
            public byte timeout_interval;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(7)]
            public reject_reason_code response_code;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct logout_request_msg
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.I2)]
            public UInt16 msg_len;

            [FieldOffset(2)]
            [MarshalAs(UnmanagedType.I1)]
            public limeq_message_type msg_type;

            [FieldOffset(3)]
            [MarshalAs(UnmanagedType.I1)]
            public Byte reserved;
        }

        //TODO: IMplement heartbeat
        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct heartbeat_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public Byte reserved;
        }


        public const int QUOTE_SOURCE_NAME_LEN = 4;
        public const int MMID_LEN = 5;

        //typedef char quote_source_id_t[QUOTE_SOURCE_NAME_LEN];
        //typedef char mmid_t[MMID_LEN];

        // Must match enum SymbolType in quoteSystemApi.h
        enum subscribe_symbol_type
        {
            // Symbol is 21 characters: OCC Symbol(6)+Expiration YYMMDD(6)+[P|C](1)+Strike(8).
            // Subscribes to the unique option series with the specified OCC Symbol, expiration, and strike.
            SUBSCRIBE_SYMBOL_TYPE_NORMAL,
            // Symbol is 21 characters: UNDERLYING(6)+Expiration YYMMDD(6)+[P|C](1)+Strike(8).
            // Subscribes to all options with the specified underlying, expiration, and strike.
            SUBSCRIBE_SYMBOL_TYPE_UNDERLYING_ATTRIBUTES,
            // Symbol is an OCC Symbol (OPRA root):
            // Subscribes to all options with the specified class.
            SUBSCRIBE_SYMBOL_TYPE_CLASS,
            // Symbol is an underlying security:
            // Subscribes to all options with the specified underlying.
            SUBSCRIBE_SYMBOL_TYPE_UNDERLYING,
            // Symbol is an index.
            // Used to subscribe all to just indexes.
            SUBSCRIBE_SYMBOL_TYPE_INDEX
        };

        // Must match enum SubscriptionType in quoteSystemApi.h
        public enum subscribe_type
        {
            SUBSCRIBE_TYPE_MARKET_DATA = 1 << 0,    // Also used for analytics by Options Analytics feed
            SUBSCRIBE_TYPE_SHOCKED_VALUES = 1 << 1, // Only supported by Options Analytics feed
            SUBSCRIBE_TYPE_ATTRIBUTES = 1 << 2
        };

        //
        // subscription_request_msg 'flags' has this format:
        //
        // Bit    0: unsubscribe
        // Bits 1-3: symbolType {normal, underlying_attributes, class, underlying, index}
        // Bit  4-6: SubscriptionTypeMask {MarketData | Attributes}
        // Bit    7: Reserved2
        //
        public const int SUBSCRIBE_TYPE_MASK_BIT_OFFSET = 4;
        public const int SUBSCRIBE_TYPE_MASK_ALL = (0xf << SUBSCRIBE_TYPE_MASK_BIT_OFFSET);
        public const int SYMBOL_TYPE_MASK_BIT_OFFSET = 1;
        public const int SYMBOL_TYPE_MASK_ALL = (0x7 << SYMBOL_TYPE_MASK_BIT_OFFSET);

        //
        // Definitions for subscription_request_msg 'flags'.
        //
        public enum subscription_flags : byte
        {
            SUBSCRIPTION_FLAG_UNSUBSCRIBE = 1,                                                            // 0=subscribe, 1=unsubscribe
            SUBSCRIPTION_FLAG_MARKET_DATA = (subscribe_type.SUBSCRIBE_TYPE_MARKET_DATA << SUBSCRIBE_TYPE_MASK_BIT_OFFSET), // subscribe for quotes&trades (or analytics from Options Analytics feed)
            SUBSCRIPTION_FLAG_SHOCKED_VALUES = (subscribe_type.SUBSCRIBE_TYPE_MARKET_DATA << subscribe_type.SUBSCRIBE_TYPE_SHOCKED_VALUES), // subscribe for shocked values (Options Analytics only)
            SUBSCRIPTION_FLAG_ATTRIBUTES = (subscribe_type.SUBSCRIBE_TYPE_ATTRIBUTES << SUBSCRIBE_TYPE_MASK_BIT_OFFSET),  // subscribe for attributes
        };

        // This message is backward compatible with subscription_request_msg.
        // We're migrating away from use of bit fields for better portability.
        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct subscription_request_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public subscription_flags flags;

            [MarshalAs(UnmanagedType.I1, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(8)]
            public UInt16 num_symbols;

            [MarshalAs(UnmanagedType.I1, SizeConst = 64)]
            [FieldOffset(10)]
            public fixed byte syb_symbols[64];
        }


        public enum subscription_outcome : byte
        {
            LOAD_ALLOCATION_EXCEEDED = (byte) 'L',
            MAX_NUMBER_OF_CLIENTS_EXCEEDED = (byte) 'M',
            //  MAX_SUBSCRIPTIONS_EXCEEDED     = 'S',
            QUOTE_SOURCE_NOT_FOUND = (byte) 'Q',
            SUBSCRIPTION_SUCCESSFUL = (byte) 'P',
            LICENSE_ALLOCATION_EXCEEDED = (byte) 'A',
            NO_LICENSE_FOR_QUOTE_SOURCE = (byte) 'N',
            SUBSCRIBE_ALL_DISABLED = (byte)  'D',
            LICENSE_IP_VALIDATION_FAILED = (byte) 'I'
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct subscription_reply_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public subscription_outcome outcome;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];
        }


        public enum book_rebuild_action
        {
            BOOK_REBUILD_STARTED = 0,
            BOOK_REBUILD_ENDED = 1
        }

        public enum valid_symbol_indicator
        {
            VALID_SYMBOL = 0,
            INVALID_SYMBOL = 1
        }

        //#define BOOK_REBUILD_ACTION_POS 0
        //#define BOOK_REBUILD_ACTION_LEN 1
        //#define VALID_SYMBOL_INDICATOR_POS (BOOK_REBUILD_ACTION_POS + BOOK_REBUILD_ACTION_LEN)
        //#define VALID_SYMBOL_INDICATOR_LEN 1
        //#define SYMBOL_LEN_POS (VALID_SYMBOL_INDICATOR_POS + VALID_SYMBOL_INDICATOR_LEN)
        //#define SYMBOL_LEN_LEN 6

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct book_rebuild_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            // byte           action : 1;
            // byte           symbol_indicator : 1;
            // byte           symbol_len : 6;
            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public byte symbol_flags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(8)]
            public UInt32 symbol_index;

            [MarshalAs(UnmanagedType.I1, SizeConst = 21)]
            [FieldOffset(12)]
            public fixed byte symbol[21];

            [MarshalAs(UnmanagedType.I1, SizeConst = 3)]
            [FieldOffset(33)]
            fixed byte pad[3];
        }


        // ORDER: Only 2 bits allocated for quote_side, value can't be bigger than 3.
        public enum quote_side : byte
        {
            NONE = 0,
            BUY = 1,
            SELL = 2
        }

        public const int QUOTE_SIDE_POS = 0;
        public const int QUOTE_SIDE_LEN = 2;

        public enum quote_flags
        {
            QUOTE_FLAGS_TOP_OF_BOOK = 1 << 0,
            QUOTE_FLAGS_NBBO = 1 << 1,
            QUOTE_FLAGS_PRICE_AGGR = 1 << 2
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct common_data
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public quote_side side;  // : 2      
            //byte           feed_specific_properties : 6; 

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(4)]
            public byte quote_flags;

            //union {                                      
            //    byte       sales_conditions;             
            //    byte       quote_flags;                  
            //    byte       byte;                         
            //} u;                                         

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(5)]
            public sbyte price_exponent;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(6)]
            public byte additional_prop;

            [MarshalAs(UnmanagedType.I1, SizeConst = 5)]
            [FieldOffset(7)]
            public fixed byte mmid[5];

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(12)]
            public UInt32 timestamp;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(16)]
            public UInt32 order_id;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(20)]
            public UInt32 shares;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(24)]
            public Int32 price_mantissa;

            [MarshalAs(UnmanagedType.I1, SizeConst = 4)]
            [FieldOffset(28)]
            public fixed byte quote_source_id[4];

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(32)]
            public UInt32 symbol_index;
        }

        public const int EXCHANGE_ORDER_ID_LEN = 8;

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct order_msg
        {
            [FieldOffset(0)]
            public common_data common;
        }

        public enum trade_msg_flags_e
        {
            TRADE_MSG_FLAGS_HIGH_TICK = 1 << 0,
            TRADE_MSG_FLAGS_LOW_TICK = 1 << 1,
            TRADE_MSG_FLAGS_CANCELLED = 1 << 2,
            TRADE_MSG_FLAGS_CORRECTED = 1 << 3,
            TRADE_MSG_FLAGS_PREVIOUS_SESSION_LAST_TRADE = 1 << 4,
            TRADE_MSG_FLAGS_LAST_TICK = 1 << 5,
            TRADE_MSG_FLAGS_OPEN_TICK = 1 << 6,
            TRADE_MSG_FLAGS_CLOSE_TICK = 1 << 7
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct trade_msg
        {
            [FieldOffset(0)]
            public common_data common;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(36)]
            public UInt32 total_volume;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(40)]
            public byte flags;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(41)]
            public byte fill1;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(42)]
            public ushort fill2;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct trade_info
        {
            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(0)]
            public Int32 price_mantissa;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(4)]
            public UInt32 shares;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(8)]
            public UInt32 timestamp;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(12)]
            public Char price_exp;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(13)]
            public Char fill1;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(14)]
            public UInt16 fill2;
        }


        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct trade_cancel_correct_msg
        {
            [FieldOffset(0)]
            public common_data common;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(36)]
            public UInt32 updated_total_volume;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(40)]
            public Int32 price_mantissa;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(44)]
            public Char price_exp;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(48)]
            public UInt32 shares;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(52)]
            public UInt32 timestamp;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct mod_execution_msg
        {
            [FieldOffset(0)]
            public common_data common;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(36)]
            public UInt32 total_volume;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(40)]
            public UInt32 shares_executed;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(44)]
            public Int32 trade_price_mantissa;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(48)]
            public Char trade_price_exponent;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(49)]
            public byte trade_flags;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(50)]
            public UInt16 pad;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(52)]
            public UInt32 trade_id;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct order_replace_msg
        {
            [FieldOffset(0)]
            public common_data common;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(36)]
            public UInt32 old_order_id;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(40)]
            public Int32 old_mantissa;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(44)]
            public Char old_exponent;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(45)]
            public char old_additional_prop;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(46)]
            public UInt16 pad;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct quote_source_ctl_msg
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.I2)]
            public UInt16 msg_len;

            [FieldOffset(2)]
            [MarshalAs(UnmanagedType.I1)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public byte message_code;

            [MarshalAs(UnmanagedType.I1, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];
        }


        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct limeq_control_common_data
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.I2)]
            public UInt16 msg_len;

            [FieldOffset(2)]
            [MarshalAs(UnmanagedType.I1)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public byte code;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(8)]
            public UInt16 common_data_len;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(10)]
            public UInt16 sub_type;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct limeq_control_msg
        {
            [FieldOffset(0)]
            [MarshalAs(UnmanagedType.I2)]
            public UInt16 msg_len;

            [FieldOffset(2)]
            [MarshalAs(UnmanagedType.I1)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(3)]
            public limeq_control_code code;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            [FieldOffset(4)]
            public fixed byte qsid[4];

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(8)]
            public UInt16 common_data_len;

            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(10)]
            public UInt16 sub_type;

            [MarshalAs(UnmanagedType.I1, SizeConst = 4)]
            [FieldOffset(12)]
            public fixed byte byte_array[4];
        }

        [StructLayout(LayoutKind.Explicit, Pack = 0)]
        public unsafe struct imbalance_msg
        {
            [MarshalAs(UnmanagedType.I2)]
            [FieldOffset(0)]
            public UInt16 msg_len;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(2)]
            public limeq_message_type msg_type;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
            [FieldOffset(3)]
            public fixed byte mmid[5];

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
            [FieldOffset(8)]
            public byte side; // : QUOTE_SIDE_LEN;
            // byte           reserved : 6;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(9)]
            public char price_exponent;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(10)]
            public byte imbalance_type;

            [MarshalAs(UnmanagedType.I1)]
            [FieldOffset(11)]
            public byte pad;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(12)]
            public UInt32 timestamp;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(16)]
            public UInt32 shares;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(20)]
            public Int32 price_mantissa;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4)]
            [FieldOffset(24)]
            public fixed byte quote_source_id[4];

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(28)]
            public UInt32 symbol_index;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(32)]
            public Int32 total_imbalance;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(36)]
            public Int32 market_imbalance;

            [MarshalAs(UnmanagedType.I4)]
            [FieldOffset(40)]
            public UInt32 feed_specific_data;
        }

        //TODO: Option message not translated.

    }
}
