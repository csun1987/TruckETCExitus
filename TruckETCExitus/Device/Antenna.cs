using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Util;

namespace TruckETCExitus.Device
{
    public class Antenna
    {
        #region 天线数据常量

        /// <summary>
        /// 预读B2帧数据长度
        /// </summary>
        public static int B2_PRE_LENGTH = 156;

        /// <summary>
        /// 交易B2帧数据长度
        /// </summary>
        public static int B2_TRD_LENGTH = 39;

        /// <summary>
        /// 交易B4帧数据长度
        /// </summary>
        public static int B4_LENGTH = 133;

        /// <summary>
        /// B9帧数据长度
        /// </summary>
        public static int B9_LENGTH = 10;                                           

        /// <summary>
        /// C1帧数据长度
        /// </summary>
        public static int C1_LENGTH = 10;

        /// <summary>
        /// D0帧数据长度
        /// </summary>
        public static int D0_LENGTH = 20;

        /// <summary>
        /// D1帧数据长度
        /// </summary>
        public static int D1_LENGTH = 38;

        /// <summary>
        /// D2帧数据长度
        /// </summary>
        public static int D2_LENGTH = 128;

        /// <summary>
        /// E4帧数据长度
        /// </summary>
        public static int E4_LENGTH = 57;

        /// <summary>
        /// 帧头
        /// </summary>
        public static byte[] bof = new byte[2] { 0xff, 0xff };

        /// <summary>
        /// 帧尾
        /// </summary>
        public static byte[] eof = new byte[1] { 0xff };

        /// <summary>
        /// 命令字位置
        /// </summary>
        public static int CMD_LOC = 3;

        /// <summary>
        /// 心跳位置
        /// </summary>
        public static int HEARTBEAT_LOC = 9;

        /// <summary>
        /// 心跳内容
        /// </summary>
        public static int HEARTBEAT_CONTENT = 0x80;

        public static byte[] SHUTDOWN_FRAME = new byte[7] { 0xFF, 0xFF, 0x87, 0x4C, 0x00, 0xCB, 0xFF };

        public static byte[] C2Frame = new byte[10] { 0xFF, 0xFF, 0x1D, 0xC2, 0x00, 0x00, 0x00, 0x00, 0xDF, 0xFF };

        #endregion

        #region 事件
        public delegate void AntennaConnHandler(CSUnit server);

        public delegate void AntennaCloseHandler(CSUnit server);

        public delegate void AntennaRecvDataHandler(CSUnit server);

        /// <summary>
        /// 客户端连接事件
        /// </summary>
        public AntennaConnHandler antennaConnHandler;

        /// <summary>
        /// 客户端关闭事件
        /// </summary>
        public AntennaCloseHandler antennaCloseHandler;

        /// <summary>
        /// 客户端接收数据事件事件
        /// </summary>
        public AntennaRecvDataHandler antennarecvDataHandler;

        #endregion

        #region 属性     

        /// <summary>
        /// 客户端类
        /// </summary>
        private Client cli;

        private bool isConnected = true;

        private System.Timers.Timer tmrBreakInterval = new System.Timers.Timer();                     // 断开时间间隔 

        private Object closeLock = new Object();

        #endregion

        #region 构造函数

        public Antenna(int accessType)
        {
            switch (accessType)
            {
                case 1:
                    cli = new CommClient();
                    break;
                case 2:
                    cli = new TCPClient();
                    break;
                default:
                    throw new Exception(string.Format("accessType错误,无法创建Antenna:{0}", accessType));
            }

            cli.clientCloseHandler = ClientCloseHandler;
            cli.clientConnHandler = ClientConnHandler;
            cli.recvDataHandler = RecvDataHandler;

            tmrBreakInterval.Interval = 70000;
            tmrBreakInterval.Enabled = true;
            tmrBreakInterval.Elapsed += new System.Timers.ElapsedEventHandler(tmrbreakIntervalElapsed);
        }

        private void tmrbreakIntervalElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this.isConnected = false;
        }

        #endregion

        #region 方法        

        public void Connect(string address, int port)
        {
            cli.Connect(address, port);

            if (cli.IsConnect())
                this.isConnected = true;
            else
                this.isConnected = false;
        }
        public bool IsConnect()
        {
            return this.isConnected;         
        }

        public void Close()
        {
            cli.Send(Antenna.SHUTDOWN_FRAME);

            cli.Close();
        }

        public void Send(byte[] buffer)
        {
            if (cli.IsConnect())
            {
                if (buffer[0] == Antenna.bof[0] && buffer[1] == Antenna.bof[1]  && 
                    buffer[buffer.Length - 1] == Antenna.eof[0] && buffer.Length >= 10 &&
                    buffer[Antenna.HEARTBEAT_LOC] == Antenna.HEARTBEAT_CONTENT &&
                    buffer[Antenna.CMD_LOC] == 0xB2)
                {
                    //心跳包
                    cli.Send(buffer);
                }
                else
                {
                    LogTools.WriteAntennaMonitorLog("天线 send:" + SystemUnit.byteToHexStr(buffer));
                    cli.Send(buffer);
                }                
            }
        }

        #endregion

        #region 事件函数

        private void ClientCloseHandler(CSUnit csUnit)
        {
            if (this.antennaCloseHandler != null)
            {
                this.antennaCloseHandler(csUnit);
            }
        }
        private void ClientConnHandler(CSUnit csUnit)
        {
            if (this.antennaConnHandler != null)
            {
                this.antennaConnHandler(csUnit);
            }
        }
        private void RecvDataHandler(CSUnit csUnit)
        {
            if (this.antennarecvDataHandler != null)
            {
                tmrBreakInterval.Enabled = false;
                this.isConnected = true;
                tmrBreakInterval.Enabled = true;
                
                if (csUnit.Buffer[0] == Antenna.bof[0] && csUnit.Buffer[1] == Antenna.bof[1] &&
                    csUnit.Buffer[csUnit.Buffer.Length - 1] == Antenna.eof[0] && csUnit.Buffer.Length >= 10 &&
                    csUnit.Buffer[Antenna.HEARTBEAT_LOC] == Antenna.HEARTBEAT_CONTENT &&
                    csUnit.Buffer[Antenna.CMD_LOC] == 0xB2)
                {
                    //心跳包
                    this.antennarecvDataHandler(csUnit);
                }
                else
                {
                    LogTools.WriteAntennaMonitorLog("天线 receive:" + SystemUnit.byteToHexStr(csUnit.Buffer));

                    this.antennarecvDataHandler(csUnit);
                }                
            }
        }

        #endregion

        public static byte[] createC1Frame(byte obuBit1, byte obuBit2, byte obuBit3, byte obuBit4)
        {
            byte[] C1Frame = new byte[C1_LENGTH];
            C1Frame[0] = bof[0];
            C1Frame[1] = bof[1];
            C1Frame[2] = 0x1D;
            C1Frame[3] = 0xC1;
            C1Frame[4] = obuBit1;
            C1Frame[5] = obuBit2;
            C1Frame[6] = obuBit3;
            C1Frame[7] = obuBit4;
            C1Frame[8] = SystemUnit.Get_CheckXor(C1Frame, C1Frame.Length);
            C1Frame[9] = 0xFF;
            return C1Frame;
        }

        public static byte[] CreateB2Frame(List<byte> D1Frame, List<byte> D2Frame)
        {
            byte[] b2frame = new byte[Antenna.B2_PRE_LENGTH];
            b2frame[0] = 0xFF;
            b2frame[1] = 0xFF;
            b2frame[2] = D2Frame[2];                                                //串口帧序列号
            b2frame[3] = 0xB2;                                                      //数据帧类型
            b2frame[4] = 0x09;                                                      //RSU类型
            Array.ConstrainedCopy(D2Frame.ToArray(), 4, b2frame, 5, 4);             //OBU号
            b2frame[9] = D2Frame[8];                                                //OBU错误状态码
            Array.ConstrainedCopy(D1Frame.ToArray(), 9, b2frame, 10, 27);           //D1帧信息
            Array.ConstrainedCopy(D2Frame.ToArray(), 9, b2frame, 37, 117);          //D2帧信息
            b2frame[154] = SystemUnit.Get_CheckXor(b2frame, b2frame.Length);        //BCC
            b2frame[155] = 0xFF;
            return b2frame;
        }
        private static byte[] GetObuByte(int obuNum)
        {
            byte[] obuBuff = new byte[4];
            obuBuff[0] = Convert.ToByte((obuNum >> 24) % 256);
            obuBuff[1] = Convert.ToByte((obuNum >> 16) % 256);
            obuBuff[2] = Convert.ToByte((obuNum >> 8) % 256);
            obuBuff[3] = Convert.ToByte((obuNum >> 0) % 256);

            return obuBuff;
        }
        public static byte[] CreateB4Frame(int obuNum ,int Axle_Type, int Whole_Weight, byte[] B2Frame, byte [] E4Frame)
        {
            byte[] B4Frame = new byte[Antenna.B4_LENGTH];
            B4Frame[0] = Antenna.bof[0];
            B4Frame[1] = Antenna.bof[1];

            B4Frame[2] = B2Frame[2];                                                    // 串口帧序列号
            B4Frame[3] = 0xB4;                                                          // 数据帧类型

            byte[] obuBuff = GetObuByte(obuNum);
            Array.ConstrainedCopy(obuBuff, 0, B4Frame, 4, 4);                           // OBU号

            B4Frame[8] = B2Frame[9];                                                    // 执行状态码
            B4Frame[9] = 0x00;                                                          // 卡类型
            Array.ConstrainedCopy(B2Frame, 150, B4Frame, 10, 4);                        // 卡余额
            Array.ConstrainedCopy(E4Frame, 9, B4Frame, 14, 46);                         // 0015文件
            Array.ConstrainedCopy(B2Frame, 83, B4Frame, 60, 43);                        // 0019文件
            Array.ConstrainedCopy(B2Frame, 126, B4Frame, 103, 24);                      // 0009文件
            //Array.ConstrainedCopy(B2Frame, 37, B4Frame, 14, 113);                     // 预读B2帧信息

            B4Frame[127] = Convert.ToByte(Axle_Type / 255 % 255);                       // 货车辆轴组
            B4Frame[128] = Convert.ToByte(Axle_Type % 255);                             // 货车辆轴组
            B4Frame[129] = Convert.ToByte(Whole_Weight / 10 / 255 % 255);               // 货车重量
            B4Frame[130] = Convert.ToByte(Whole_Weight / 10 % 255);                     // 货车重量
            B4Frame[131] = SystemUnit.Get_CheckXor(B4Frame, B4Frame.Length);            // BCC
            B4Frame[132] = Antenna.eof[0];

            return B4Frame;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="RSCTL">串口帧序列号</param>
        /// <param name="errorCode">执行状态码</param>
        /// <param name="N1">预读队列</param>
        /// <param name="ReserveA">预读状态列表</param>
        /// <param name="ReserveB">交易状态列表</param>
        /// <returns></returns>
        public static byte[] createB9Frame(byte RSCTL, byte errorCode, byte N1, byte ReserveA, byte ReserveB)
        {
            byte[] b9frame = new byte[Antenna.B9_LENGTH];
            b9frame[0] = 0xFF;
            b9frame[1] = 0xFF;
            b9frame[2] = RSCTL;                                                  //串口帧序列号
            b9frame[3] = 0xB9;                                                   //数据帧类型
            b9frame[4] = errorCode;                                              //执行状态码
            b9frame[5] = N1;                                                     //预读队列
            b9frame[6] = ReserveA;                             //预读状态列表
            b9frame[7] = ReserveB;                                 //预读状态列表


            b9frame[8] = SystemUnit.Get_CheckXor(b9frame, b9frame.Length);       //BCC
            b9frame[9] = 0xFF;
            return b9frame;
        }
    }
}
