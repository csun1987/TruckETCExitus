﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TruckETCExitus.Device;
using TruckETCExitus.Etc;
using Util;

namespace TruckETCExitus.Model
{
    class ExitusLiZhiCoilState : State
    {
        private int preStep = 0;                                                            // 预读步骤

        private int trdStep = 0;                                                            // 交易步骤

        private int preAntanaOBUNo = -1;                                                    // 预读中的OBU号

        private int trdAntanaOBUNo = -1;                                                    // 交易中的OBU号

        private byte[] B2Frame = new byte[Antenna.B2_PRE_LENGTH];                           // B2帧，存储预读天线B2帧数据

        private List<byte> D1Frame = new List<byte>();                                      // D1数据帧内容        

        System.Timers.Timer tmrPre = new System.Timers.Timer();                             // 预读超时重置      

        System.Timers.Timer tmrTrd = new System.Timers.Timer();                             // 交易超时重置

        private int g_u8waitPrdCarNum = 0;

        private int g_u8PrdCarNum = 0;

        private int g_u8TrdCarNum = 0;

        private int g_u8PassStatus = 0;

        private enum AlartStat
        {
            NonAlarmed = 1,
            Alarmed,
            HalfAlarmed 
        };
        private AlartStat prdAlartStat = AlartStat.NonAlarmed;

        private AlartStat trdAlartStat = AlartStat.NonAlarmed;           

        public void InitControl(Button btnUICtrl)
        {
            tmrPre.Interval = 200;
            tmrPre.Enabled = false;
            tmrPre.Elapsed += new System.Timers.ElapsedEventHandler(tmrPreElapsed);

            tmrTrd.Interval = 2000;
            tmrTrd.Enabled = false;
            tmrTrd.Elapsed += new System.Timers.ElapsedEventHandler(tmrTrdElapsed);

            if (InitParams.UIEnabled)
            {
                btnUICtrl.Text = "关闭UI";
            }
            else
            {
                btnUICtrl.Text = "启动UI";
            }
        }
        private void tmrPreElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ResetPreStep();
        }
        private void tmrTrdElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            ResetTrdStep();
        }
        #region 预读天线
        public void HandlePreAntennaConn(CSUnit csUnit, RichTextBox rtb)
        {
            UpdatePreAntannaMsg(csUnit, "连接", Color.Blue, rtb);

            ResetPreStep();
        }

        public void HandlePreAntennaClose(CSUnit csUnit, RichTextBox rtb)
        {
            UpdatePreAntannaMsg(csUnit, "断开", Color.Green, rtb);

            ResetPreStep();
        }

        public void HandlePreAntRecvData(CSUnit csUnit, RichTextBox rtb)
        {
            if (csUnit.Buffer[0] == Antenna.bof[0] && csUnit.Buffer[1] == Antenna.bof[1]
                && csUnit.Buffer[csUnit.Buffer.Length - 1] == Antenna.eof[0] && csUnit.Buffer.Length >= 6)
            {
                switch (csUnit.Buffer[Antenna.CMD_LOC])
                {
                    case 0xB0:
                        HandlePreB0Frame(csUnit, rtb);
                        break;
                    case 0xB2:
                        HandlePreB2Frame(csUnit, rtb);
                        break;
                    case 0xD1:
                        tmrPre.Enabled = true;
                        if (g_u8waitPrdCarNum > 0)
                        {
                            HandleD1Frame(csUnit, rtb);
                        }
                        else
                        {
                            Global.PreAntenna.Send(Antenna.C2Frame);
                            ResetPreStep();
                        }                     
                        break;
                    case 0xD2:
                        HandleD2Frame(csUnit, rtb);
                        break;
                    default:
                        HandlePreAntDefaultFrame(csUnit, rtb);
                        break;
                }
            }
            else
            {
                UpdatePreAntannaMsg(csUnit, "异常帧", Color.Red, rtb);
            }
        }

        private void ResetPreStep()
        {
            tmrPre.Enabled = false;
            preAntanaOBUNo = -1;
            preStep = 0;
        }

        private void UpdatePreAntannaMsg(CSUnit csUnit, string msg, Color color, RichTextBox rtb)
        {
            if (InitParams.UIEnabled)
            {
                string info = string.Format("{0}---预读天线(IP:{1},Port:{2})=>>CRTC---{3}\r\n",
                            System.DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss:fff"), csUnit.IpOrCommPort,
                            csUnit.PortOrBaud, msg);

                RichTextBoxUtil.UpdateRTxtUI(rtb, info, color);
            }
        }

        /// <summary>
        /// 收到B0直接转发给PC
        /// </summary>
        /// <param name="csUnit">远程单元</param>
        private void HandlePreB0Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                Global.localServer.Send(csUnit.Buffer);

            UpdatePreAntannaMsg(csUnit, "预读B0到来", Color.Black, rtb);
            ResetPreStep();
        }

        /// <summary>
        /// 预读B2，只可能为心跳
        /// </summary>
        /// <param name="csUnit">远程单元</param>
        private void HandlePreB2Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                Global.localServer.Send(csUnit.Buffer);
        }

        /// <summary>
        /// 只有预读步骤为0时执行(即第一次收到D1,tmrPre后重置).
        /// 收到后记录OBU号,回复C1.
        /// 根据D1、D2内容生成预读B2.
        /// </summary>
        /// <param name="csUnit">远程单元</param>
        private void HandleD1Frame(CSUnit csUnit, RichTextBox rtb)
        {
            string info = "";
            if (preStep == 0)
            {
                D1Frame.Clear();
                D1Frame = ByteFilter.deFilter(csUnit.Buffer);

                preAntanaOBUNo = csUnit.Buffer[4] * 16777216 + csUnit.Buffer[5] * 65536
                    + csUnit.Buffer[6] * 256 + csUnit.Buffer[7];

                if (D1Frame.Count == Antenna.D1_LENGTH)
                {
                    byte[] C1Frame = Antenna.createC1Frame(csUnit.Buffer[4], csUnit.Buffer[5], csUnit.Buffer[6], csUnit.Buffer[7]);

                    if (Global.PreAntenna.IsConnect())
                        Global.PreAntenna.Send(C1Frame);

                    preStep = 1;
                    UpdatePreAntannaMsg(csUnit, "D1到来", Color.BlueViolet, rtb);
                }
                else
                {
                    UpdatePreAntannaMsg(csUnit, "D1长度错误", Color.CadetBlue, rtb);
                    ResetPreStep();
                }
            }
            else
            {
                UpdatePreAntannaMsg(csUnit, "D1到来顺序错误", Color.Red, rtb);
                ResetPreStep();
            }
        }

        /// <summary>
        /// 只有预读步骤为1时执行(即收到D1后的D2才接收,tmrPre后重置).
        /// 收到后对比D1 obu号,正确则D1、D2组合B2 发送PC机.
        /// </summary>
        /// <param name="csUnit">远程单元</param>
        private void HandleD2Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (preStep == 1)
            {
                List<byte> D2Frame = ByteFilter.deFilter(csUnit.Buffer);

                if (D2Frame.Count == 128)
                {
                    int d1OBUNo = csUnit.Buffer[4] * 16777216 + csUnit.Buffer[5] * 65536
                    + csUnit.Buffer[6] * 256 + csUnit.Buffer[7];
                    if (d1OBUNo == preAntanaOBUNo)
                    {
                        B2Frame = Antenna.CreateB2Frame(D1Frame, D2Frame);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(ByteFilter.enFilter(B2Frame));

                        preStep = 2;
                        UpdatePreAntannaMsg(csUnit, "D2到来", Color.Black, rtb);
                    }
                    else
                    {
                        UpdatePreAntannaMsg(csUnit, "D2 obu号和D1不同", Color.Red, rtb);
                        ResetPreStep();
                    }
                }
                else
                {
                    UpdatePreAntannaMsg(csUnit, "D2长度错误", Color.Red, rtb);
                    ResetPreStep();
                }
            }
            else
            {
                UpdatePreAntannaMsg(csUnit, "D2到来顺序错误", Color.Red, rtb);
                ResetPreStep();
            }
        }

        private void HandlePreAntDefaultFrame(CSUnit csUnit, RichTextBox rtb)
        {
            UpdatePreAntannaMsg(csUnit, "无此帧信息:" + csUnit.Buffer[Antenna.CMD_LOC].ToString("X2"), Color.Red, rtb);
            ResetPreStep();
        }

        #endregion

        #region 交易天线

        public void HandleTrdAntennaConn(CSUnit csUnit, RichTextBox rtb)
        {
            UpdateTrdAntannaMsg(csUnit, "连接", Color.Blue, rtb);

            ResetTrdStep();
        }

        public void HandleTrdAntennaClose(CSUnit csUnit, RichTextBox rtb)
        {
            UpdateTrdAntannaMsg(csUnit, "断开", Color.Green, rtb);

            ResetTrdStep();
        }

        public void HandleTrdAntRecvData(CSUnit csUnit, RichTextBox rtb)
        {
            if (csUnit.Buffer[0] == Antenna.bof[0] && csUnit.Buffer[1] == Antenna.bof[1]
                && csUnit.Buffer[csUnit.Buffer.Length - 1] == Antenna.eof[0] && csUnit.Buffer.Length >= 6)
            {
                switch (csUnit.Buffer[Antenna.CMD_LOC])
                {
                    case 0xB0:
                        HandleTrdB0Frame(csUnit, rtb);
                        break;
                    case 0xB2:
                        tmrTrd.Enabled = true;
                        HandleB2Frame(csUnit, rtb);
                        break;
                    case 0xB3:
                        HandleB3Frame(csUnit, rtb);
                        break;
                    case 0xB5:
                        HandleB5Frame(csUnit, rtb);
                        break;
                    default:
                        HandleTrdAntDefaultFrame(csUnit, rtb);
                        break;
                }
            }
            else
            {
                UpdateTrdAntannaMsg(csUnit, "异常帧", Color.Red, rtb);
            }
        }
        private void ResetTrdStep()
        {
            trdAntanaOBUNo = -1;
            trdStep = 0;
            tmrTrd.Enabled = false;
        }

        private void UpdateTrdAntannaMsg(CSUnit csUnit, string msg, Color color, RichTextBox rtb)
        {
            if (InitParams.UIEnabled)
            {
                string info = string.Format("{0}---交易天线(IP:{1},Port:{2})=>>CRTC---{3}\r\n",
                            System.DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss:fff"), csUnit.IpOrCommPort,
                            csUnit.PortOrBaud, msg);

                RichTextBoxUtil.UpdateRTxtUI(rtb, info, color);
            }
        }

        /// <summary>
        /// 收到B0直接转发给PC
        /// </summary>
        /// <param name="csUnit">远程单元</param>
        private void HandleTrdB0Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                Global.localServer.Send(csUnit.Buffer);

            UpdateTrdAntannaMsg(csUnit, "收到交易B0帧", Color.GreenYellow, rtb);
            ResetTrdStep();
        }

        private void HandleB2Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (csUnit.Buffer[Antenna.HEARTBEAT_LOC] == Antenna.HEARTBEAT_CONTENT)
            {
                //心跳包
                if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                    Global.localServer.Send(csUnit.Buffer);
            }
            else
            {
                if (trdStep == 0)
                {
                    List<byte> B2frame = ByteFilter.deFilter(csUnit.Buffer);
                    if (B2frame.Count == Antenna.B2_TRD_LENGTH)
                    {
                        trdAntanaOBUNo = csUnit.Buffer[5] * 16777216 + csUnit.Buffer[6] * 65536
                        + csUnit.Buffer[7] * 256 + csUnit.Buffer[8];
                        /////////////////////////////////////////////////////////////////////////////////////
                        OBUData trdObu;
                        if (Global.exchangeQueue.obuQueue.Count > 0)
                            trdObu = Global.exchangeQueue.obuQueue.Peek();
                        else
                            trdObu = new OBUData(0);
                        if(trdObu.ObuNum != trdAntanaOBUNo)
                        {
                            Global.TrdAntenna.Send(Antenna.C2Frame);
                            ResetTrdStep();
                            return;
                        }
                        /////////////////////////////////////////////////////////////////////////////////////
                        trdStep = 1;

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(csUnit.Buffer);
                        UpdateTrdAntannaMsg(csUnit, "B2帧到来", Color.CornflowerBlue, rtb);
                    }
                    else
                    {
                        UpdateTrdAntannaMsg(csUnit, "B2帧长度异常", Color.Red, rtb);
                        ResetTrdStep();
                    }
                }
                else
                {
                    UpdateTrdAntannaMsg(csUnit, "B2帧到来顺序异常:" + trdStep, Color.Red, rtb);
                    ResetTrdStep();
                }

            }
        }

        private void HandleB3Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (trdStep == 1)
            {
                if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                    Global.localServer.Send(csUnit.Buffer);
                trdStep = 2;
                UpdateTrdAntannaMsg(csUnit, "B3到来", Color.Blue, rtb);
            }
            else
            {
                UpdateTrdAntannaMsg(csUnit, "B3帧到来顺序异常:" + trdStep, Color.Red, rtb);
                ResetTrdStep();
            }
        }

        private void HandleB5Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (trdStep == 3)
            {
                if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                    Global.localServer.Send(csUnit.Buffer);

                trdStep = 4;
                UpdateTrdAntannaMsg(csUnit, "B5到来", Color.BlueViolet, rtb);
            }
            else
            {
                UpdateTrdAntannaMsg(csUnit, "B5帧到来顺序异常:" + trdStep, Color.Red, rtb);
                ResetTrdStep();
            }
        }

        private void HandleTrdAntDefaultFrame(CSUnit csUnit, RichTextBox rtb)
        {
            UpdateTrdAntannaMsg(csUnit, "无此帧信息:" + csUnit.Buffer[Antenna.CMD_LOC].ToString("X2"), Color.Red, rtb);
            ResetTrdStep();
        }
        #endregion

        #region 本地服务器
        public void HandleLocSrvConn(CSUnit csUnit, RichTextBox rtb)
        {
            UpdateLocSrvMsg(csUnit, "连接", Color.Blue, rtb);
        }

        public void HandleLocSrvClose(CSUnit csUnit, RichTextBox rtb)
        {
            UpdateLocSrvMsg(csUnit, "断开", Color.Green, rtb);
        }

        public void HandleLocSrvRecvData(CSUnit csUnit, RichTextBox rtb)
        {
            if (csUnit.Buffer[0] == LocalServer.bof[0] && csUnit.Buffer[1] == LocalServer.bof[1] && csUnit.Buffer[csUnit.Buffer.Length - 1] == LocalServer.eof[0] &&
                csUnit.Buffer.Length >= 6)
            {
                switch (csUnit.Buffer[LocalServer.CMD_LOC])
                {
                    case 0x4C:
                        Handle4CFrame(csUnit, rtb);
                        break;
                    case 0xC0:
                        HandleC0Frame(csUnit, rtb);
                        break;
                    case 0xC1:
                        HandleC1Frame(csUnit, rtb);
                        break;
                    case 0xC2:
                        HandleC2Frame(csUnit, rtb);
                        break;
                    case 0xC3:
                        HandleC3Frame(csUnit, rtb);
                        break;
                    case 0xC6:
                        HandleC6Frame(csUnit, rtb);
                        break;
                    case 0xCD:
                        HandleCDFrame(csUnit, rtb);
                        break;
                    default:
                        UpdateLocSrvMsg(csUnit, "无此帧", Color.OrangeRed, rtb);
                        break;
                }
            }
            else
            {
                UpdateLocSrvMsg(csUnit, "异常帧", Color.Purple, rtb);
            }
        }
       

        private void UpdateLocSrvMsg(CSUnit csUnit, string msg, Color color, RichTextBox rtb)
        {
            if (InitParams.UIEnabled)
            {
                string info = string.Format("{0}---PC(IP:{1},Port:{2})=>>CRTC---{3}\r\n",
                            System.DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss:fff"), csUnit.IpOrCommPort,
                            csUnit.PortOrBaud, msg);

                RichTextBoxUtil.UpdateRTxtUI(rtb, info, color);
            }
        }
        private void Handle4CFrame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.PreAntenna != null && Global.PreAntenna.IsConnect())
                Global.PreAntenna.Send(csUnit.Buffer);
            if (Global.TrdAntenna != null && Global.TrdAntenna.IsConnect())
                Global.TrdAntenna.Send(csUnit.Buffer);

            UpdateLocSrvMsg(csUnit, "关闭天线", Color.OrangeRed, rtb);
        }

        private void HandleC0Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.PreAntenna != null && Global.PreAntenna.IsConnect())
                Global.PreAntenna.Send(csUnit.Buffer);
            if (Global.TrdAntenna != null && Global.TrdAntenna.IsConnect())
                Global.TrdAntenna.Send(csUnit.Buffer);

            UpdateLocSrvMsg(csUnit, "初始化帧", Color.Yellow, rtb);

        }
        private void HandleC1Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (csUnit.Buffer.Length == LocalServer.C1_LENGTH && csUnit.Buffer[0] == LocalServer.bof[0]
                && csUnit.Buffer[1] == LocalServer.bof[1] && csUnit.Buffer[csUnit.Buffer.Length - 1] == LocalServer.eof[0]
                && csUnit.Buffer[LocalServer.CMD_LOC] == 0xC1)
            {
                if (preStep == 0 && trdStep == 0)
                {
                    if (Global.PreAntenna != null && Global.PreAntenna.IsConnect())
                        Global.PreAntenna.Send(csUnit.Buffer);
                    if (Global.TrdAntenna != null && Global.TrdAntenna.IsConnect())
                        Global.TrdAntenna.Send(csUnit.Buffer);

                    ResetPreStep();
                    ResetTrdStep();
                    UpdateLocSrvMsg(csUnit, "初始化确认帧", Color.Green, rtb);
                    return;
                }
                int c1OBUNo = csUnit.Buffer[4] * 16777216 + csUnit.Buffer[5] * 65536
                    + csUnit.Buffer[6] * 256 + csUnit.Buffer[7];
                if (c1OBUNo == preAntanaOBUNo)
                {
                    if (preStep == 2)
                    {
                        if (Global.PreAntenna.IsConnect())
                            Global.PreAntenna.Send(csUnit.Buffer);

                        // 收到C1并且处于未报警状态，obu数据进入队列
                        if (prdAlartStat == AlartStat.NonAlarmed)
                        {
                            PreProcessSucess(c1OBUNo);
                            if (g_u8waitPrdCarNum > 0)
                            {
                                g_u8waitPrdCarNum -= 1;
                            }
                            g_u8PrdCarNum += 1;
                        }

                        ResetPreStep();
                        UpdateLocSrvMsg(csUnit, "收到预读B2后C1", Color.LightGreen, rtb);
                        return;
                    }
                }
                else
                {
                    if (c1OBUNo == trdAntanaOBUNo)
                    {
                        if (trdStep == 1)
                        {
                            if (Global.TrdAntenna.IsConnect())
                                Global.TrdAntenna.Send(csUnit.Buffer);

                            UpdateLocSrvMsg(csUnit, "收到交易B2后C1", Color.LightSeaGreen, rtb);
                            return;
                        }
                        if (trdStep == 2)
                        {
                            VehData vehData = null;
                            OBUData obuData = null;
                            if (Global.exchangeQueue.vehQueue.Count > 0)
                                vehData = Global.exchangeQueue.vehQueue.Peek();
                            else
                                vehData = new VehData();
                            if (Global.exchangeQueue.obuQueue.Count > 0)
                                obuData = Global.exchangeQueue.obuQueue.Peek();
                            else
                                obuData = new OBUData(0);

                            if (obuData.ObuNum == c1OBUNo)
                            {
                                //byte[] B4Frame = Antenna.CreateB4Frame(c1OBUNo, vehData.Axle_Type, vehData.Whole_Weight, B2Frame);
                                //if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                                //    Global.localServer.Send(ByteFilter.enFilter(B4Frame));
                                //if (Global.TrdAntenna.IsConnect())
                                //    Global.TrdAntenna.Send(csUnit.Buffer);

                                //trdStep = 3;
                                //UpdateLocSrvMsg(csUnit, "收到交易B3后C1,转发,发送B4", Color.MediumPurple, rtb);
                                return;
                            }
                            else
                            {
                                ResetTrdStep();
                                UpdateLocSrvMsg(csUnit, "收到交易B3后C1(C1 OBU号和交易队列OBU号不同)", Color.DarkRed, rtb);
                                return;
                            }                          
                        }
                        if (trdStep == 4)
                        {
                            if (Global.TrdAntenna.IsConnect())
                                Global.TrdAntenna.Send(csUnit.Buffer);

                            Global.exchangeQueue.vehQueue.Dequeue();
                            Global.exchangeQueue.obuQueue.Dequeue();
                            g_u8TrdCarNum += 1;
                            ResetTrdStep();
                            UpdateLocSrvMsg(csUnit, "收到交易B5后C1,交易成功", Color.Purple, rtb);
                            return;
                        }
                    }
                    else
                    {
                        ResetPreStep();
                        ResetTrdStep();

                        UpdateLocSrvMsg(csUnit, "收到预读或交易后C1号码不存在", Color.DarkRed, rtb);
                    }
                }               
              
            }
        }
        private void HandleC2Frame(CSUnit csUnit, RichTextBox rtb)
        {
            int c2OBUNo = csUnit.Buffer[4] * 16777216 + csUnit.Buffer[5] * 65536
                   + csUnit.Buffer[6] * 256 + csUnit.Buffer[7];
 
            if (c2OBUNo == preAntanaOBUNo)
            {
                if(prdAlartStat == AlartStat.NonAlarmed)
                {
                    prdAlartStat = AlartStat.Alarmed;

                    Global.prdAlartStat1 = Global.AlartStat.Alarmed;

                    byte[] b9frame = createB9Frame(0x0d, 0, 0, 2, 0);

                    if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                        Global.localServer.Send(b9frame);

                }
                ResetPreStep();
            }
            else
            {
                ResetPreStep();
                UpdateLocSrvMsg(csUnit, "收到预读后C2", Color.LightGreen, rtb);
                return;
            }
            if (c2OBUNo == trdAntanaOBUNo)
            {
                if (trdAlartStat == AlartStat.NonAlarmed)
                {
                    trdAlartStat = AlartStat.Alarmed;

                    Global.trdAlartStat1 = Global.AlartStat.Alarmed;
                    byte[] b9frame = createB9Frame(0x0d, 0, 0, 0, 2);
                    if(prdAlartStat == AlartStat.NonAlarmed)
                    {
                        prdAlartStat = AlartStat.Alarmed;

                        Global.prdAlartStat1 = Global.AlartStat.Alarmed;
                        b9frame = createB9Frame(0x0d, 0, 0, 2, 2);
                    }
                    if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                        Global.localServer.Send(b9frame);

                }
                ResetTrdStep();
            }
            else
            {
                ResetTrdStep();
                UpdateLocSrvMsg(csUnit, "收到交易后C2", Color.LightGreen, rtb);
                return;
            }
            
        }
        private void PreProcessSucess(int c1OBUNo)
        {
            Global.preOBUQueue.Enqueue(new OBUData(c1OBUNo));
        }
        private void HandleC3Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.TrdAntenna.IsConnect())
            {
                Global.TrdAntenna.Send(csUnit.Buffer);
            }

            UpdateLocSrvMsg(csUnit, "收到C3", Color.DarkGreen, rtb);
        }

        private void HandleC6Frame(CSUnit csUnit, RichTextBox rtb)
        {
            if (Global.TrdAntenna.IsConnect())
            {
                Global.TrdAntenna.Send(csUnit.Buffer);
            }

            UpdateLocSrvMsg(csUnit, "收到C6", Color.DarkGreen, rtb);

        }
        private void HandleCDFrame(CSUnit csUnit, RichTextBox rtb)
        {
            g_u8TrdCarNum = 0;
            if (Global.coils.CoilStatus[6].StatNow == Coil.CurStatus.Sheltered)
            {
                trdAlartStat = AlartStat.HalfAlarmed;

                Global.trdAlartStat1 = Global.AlartStat.HalfAlarmed;
            }
            else//交易链路释放
            {
                trdAlartStat = AlartStat.NonAlarmed;

                Global.trdAlartStat1 = Global.AlartStat.NonAlarmed;
                byte[] b9frame = createB9Frame(0x0d, 0, 0, 0, 1);

                if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                    Global.localServer.Send(b9frame);
            }

            int CDobuNo = (int)(csUnit.Buffer[4] * Math.Pow(2, 24) + csUnit.Buffer[5] * Math.Pow(2, 16) +
                csUnit.Buffer[6] * Math.Pow(2, 8) + csUnit.Buffer[7] * Math.Pow(2, 0));
            OBUData cdOBU = new OBUData(CDobuNo);

            try
            {
                if (Global.exchangeQueue.obuQueue.Contains(cdOBU))
                {
                    while (Global.exchangeQueue.obuQueue.Count > 0)
                    {
                        OBUData obuData = Global.exchangeQueue.obuQueue.Peek();
                        if (obuData.Equals(cdOBU))
                        {
                            Global.exchangeQueue.obuQueue.Dequeue();
                            Global.exchangeQueue.vehQueue.Dequeue();
                            break;
                        }
                        else
                        {
                            Global.exchangeQueue.obuQueue.Dequeue();
                            Global.exchangeQueue.vehQueue.Dequeue();
                        }
                    }

                }
            }
            catch (Exception ex)
            {
                throw new Exception("CD帧 obu数据出队列报错");
            }
        }
        #endregion

        #region 线圈
        /// <summary>
        /// 
        /// </summary>
        /// <param name="RSCTL">串口帧序列号</param>
        /// <param name="errorCode">执行状态码</param>
        /// <param name="N1">预读队列</param>
        /// <param name="ReserveA">预读状态列表</param>
        /// <param name="ReserveB">交易状态列表</param>
        /// <returns></returns>
        private byte[] createB9Frame(byte RSCTL, byte errorCode, byte N1, byte ReserveA, byte ReserveB)
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
        private void preOBUDataToPreWeightQueue()
        {
            if (Global.preOBUQueue.Count > 0)
                Global.preQueue.obuQueue.Enqueue(Global.preOBUQueue.Dequeue());
            else
                throw new Exception("预读obu队列进入称重队列时：obu队列数量为0");
        }
        public void HandleRefreshCoilStatus()
        {
            // #1触发
            if (Global.coils.CoilStatus[0].CoilStat == Coil.CoilStatus.Trigger)
            {
                //跟车干扰判断
                if (g_u8waitPrdCarNum == 0)
                {
                    g_u8waitPrdCarNum += 1;
                }
                else
                {
                    if (prdAlartStat == AlartStat.NonAlarmed)//预读链路锁定
                    {
                        prdAlartStat = AlartStat.Alarmed;

                        Global.prdAlartStat1 = Global.AlartStat.Alarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 2, 0);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
                // 处于报警状态，#1触发，#2未遮挡，#3未遮挡，还原报警状态
                if (trdAlartStat == AlartStat.NonAlarmed && Global.coils.CoilStatus[1].StatNow == Coil.CurStatus.NonSheltered
                    && Global.coils.CoilStatus[1].StatNow == Coil.CurStatus.NonSheltered)
                {
                    if (prdAlartStat == AlartStat.Alarmed)//预读链路释放
                    {
                        prdAlartStat = AlartStat.NonAlarmed;

                        Global.prdAlartStat1 = Global.AlartStat.NonAlarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 1, 0);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
            }
            // #2收尾
            if (Global.coils.CoilStatus[1].CoilStat == Coil.CoilStatus.End)
            {
                //线圈3不遮挡时，还原报警状态
                if (Global.coils.CoilStatus[2].StatNow == Coil.CurStatus.NonSheltered && trdAlartStat == AlartStat.NonAlarmed)
                {
                    if (prdAlartStat == AlartStat.Alarmed)//预读链路释放
                    {
                        prdAlartStat = AlartStat.NonAlarmed;

                        Global.prdAlartStat1 = Global.AlartStat.NonAlarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 1, 0);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
            }
            // #3触发，处于未报警状态，无obu数据，则进入报警状态
            if (Global.coils.CoilStatus[2].CoilStat == Coil.CoilStatus.Trigger)
            {
                if (g_u8PrdCarNum > 0)
                {
                    g_u8PrdCarNum = g_u8PrdCarNum - 1;
                }
                else
                {
                    if (prdAlartStat == AlartStat.NonAlarmed)//预读链路锁定
                    {
                        prdAlartStat = AlartStat.Alarmed;

                        Global.prdAlartStat1 = Global.AlartStat.Alarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 2, 0);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
            }
            // #4触发
            if (Global.coils.CoilStatus[3].CoilStat == Coil.CoilStatus.Trigger)
            {
                g_u8PassStatus = 0;
            }
            // #4收尾，处于未报警状态，预读队列obu数据进入待交易队列
            if (Global.coils.CoilStatus[3].CoilStat == Coil.CoilStatus.End)
            {
                if (prdAlartStat == AlartStat.NonAlarmed && Global.coils.CoilStatus[3].CoilStat == Coil.CoilStatus.End
                && Global.raster.CurStatus == Raster.CurStat.Sheltered && g_u8PassStatus == 1)
                {
                    preOBUDataToPreWeightQueue();
                }
            }
            //预读区域变量初始化
            if (Global.coils.CoilStatus[0].StatNow == Coil.CurStatus.NonSheltered
                && Global.coils.CoilStatus[1].StatNow == Coil.CurStatus.NonSheltered &&
                Global.coils.CoilStatus[2].StatNow == Coil.CurStatus.NonSheltered)
            {
                g_u8PrdCarNum = 0;
                g_u8waitPrdCarNum = 0;
                if (Global.coils.CoilStatus[3].StatNow == Coil.CurStatus.NonSheltered)
                {
                    //预读队列全部清除
                    if (Global.preOBUQueue.Count > 0)//允许放行的车辆数为0
                    {
                        byte[] b9frame = createB9Frame(0x0d, 0, 2, 0, 0);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                    while (Global.preOBUQueue.Count > 0)
                    {
                        Global.preOBUQueue.Dequeue();                       
                    }
                }
                else
                {
                    //预读队列只保留头队列，其余数据全部清除
                    if (Global.preOBUQueue.Count > 0)
                    {
                        if (Global.preOBUQueue.Count > 1)//允许放行的车辆数为1
                        {
                            byte[] b9frame = createB9Frame(0x0d, 0, 1, 0, 0);

                            if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                                Global.localServer.Send(b9frame);
                        }
                        OBUData firstOub = Global.preOBUQueue.Dequeue();                       
                        while (Global.preOBUQueue.Count > 0)
                        {
                            Global.preOBUQueue.Dequeue();
                        }
                        Global.preOBUQueue.Enqueue(firstOub);                       
                    }
                    else
                    {
                        throw new Exception("aa");
                    }
                }
            }
            //线圈7触发
            if (Global.coils.CoilStatus[6].CoilStat == Coil.CoilStatus.Trigger)
            {
                if (g_u8TrdCarNum > 0)
                {
                    g_u8TrdCarNum = g_u8TrdCarNum - 1;
                }
                else
                {
                    if (trdAlartStat != AlartStat.Alarmed)//交易链路锁定
                    {
                        trdAlartStat = AlartStat.Alarmed;

                        Global.trdAlartStat1 = Global.AlartStat.Alarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 0, 2);
                        if (prdAlartStat == AlartStat.NonAlarmed)//预读链路锁定
                        {
                            prdAlartStat = AlartStat.Alarmed;

                            Global.prdAlartStat1 = Global.AlartStat.Alarmed;
                            b9frame = createB9Frame(0x0d, 0, 0, 2, 2);
                        }

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
            }
            //线圈8收尾
            if (Global.coils.CoilStatus[7].CoilStat == Coil.CoilStatus.End)
            {
                if (trdAlartStat == AlartStat.HalfAlarmed)
                {
                    if (Global.coils.CoilStatus[6].StatNow == Coil.CurStatus.Sheltered)//交易链路锁定
                    {
                        trdAlartStat = AlartStat.Alarmed;

                        Global.trdAlartStat1 = Global.AlartStat.Alarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 0, 2);
                        if (prdAlartStat == AlartStat.NonAlarmed)//预读链路锁定
                        {
                            prdAlartStat = AlartStat.Alarmed;

                            Global.prdAlartStat1 = Global.AlartStat.Alarmed;
                            b9frame = createB9Frame(0x0d, 0, 0, 2, 2);
                        }

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                    else//交易链路释放
                    {
                        trdAlartStat = AlartStat.NonAlarmed;

                        Global.trdAlartStat1 = Global.AlartStat.NonAlarmed;
                        byte[] b9frame = createB9Frame(0x0d, 0, 0, 0, 1);

                        if (Global.localServer != null && Global.localServer.GetConnectionCount() > 0)
                            Global.localServer.Send(b9frame);
                    }
                }
            }
        }

        #endregion

        #region 仪表

        private void UpdateMeterMsgUI(string msg, Color color, RichTextBox rtb)
        {
            if (InitParams.UIEnabled)
            {
                string info = string.Format("{0}---仪表=>>CRTC---{1}\r\n",
                            System.DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss:fff"), msg);

                RichTextBoxUtil.UpdateRTxtUI(rtb, info, color);
            }
        }

        public void HandleDataFrameComeMsg(byte[] buffer, RichTextBox rtb)
        {
            if (DataCollectorParams.remoteAccessType != 0)
            {
                DataCollector.WtSys_GetDataFrame(buffer, buffer.Length);
                Global.dataCollector.WriteRemote(buffer, 0, buffer.Length);
            }

            UpdateMeterMsgUI("数据帧到来", Color.Black, rtb);
        }

        private void preDataToExchangeQueue()
        {
            VehData vehData = Global.preQueue.vehQueue.Dequeue();
            switch (Global.preQueue.obuQueue.Count)
            {
                case 0:
                    Global.exchangeQueue.obuQueue.Enqueue(new OBUData(0));
                    Global.exchangeQueue.vehQueue.Enqueue(vehData.Clone());
                    break;
                case 1:
                    Global.exchangeQueue.obuQueue.Enqueue(Global.preQueue.obuQueue.Dequeue());
                    Global.exchangeQueue.vehQueue.Enqueue(vehData.Clone());
                    break;
                default:
                    for (int i = 0; i < Global.preQueue.obuQueue.Count - 1; i++)
                    {
                        Global.exchangeQueue.obuQueue.Enqueue(Global.preQueue.obuQueue.Dequeue());
                        Global.exchangeQueue.vehQueue.Enqueue(new VehData());
                    }
                    Global.exchangeQueue.obuQueue.Enqueue(Global.preQueue.obuQueue.Dequeue());
                    Global.exchangeQueue.vehQueue.Enqueue(vehData.Clone());
                    break;
            }
        }

        public void HandleVehComeMsg(int tmp, VehData vehData, RichTextBox rtb)
        {
            if (tmp == 1)
            {
                Global.preQueue.vehQueue.Enqueue(vehData);
                preDataToExchangeQueue();
                
                UpdateMeterMsgUI("称重数据到来", Color.Blue, rtb);
            }
            else
            {
                UpdateMeterMsgUI("称重数据到来错误!", Color.Red, rtb);
            }
        }

        public void HandleVehRasterComeMsg(RichTextBox rtb)
        {
            //光栅触发
            if (Global.raster.RasterStatus == Raster.RasterStat.Trigger)
            {
                //线圈4遮挡
                if (Global.coils.CoilStatus[3].StatNow == Coil.CurStatus.Sheltered)
                {
                    g_u8PassStatus = 1;
                }
                //if((Global.raster.VehStatWord & 1) ==0 && Global.preQueue.obuQueue.Count>0 && Global.raster.VehCount>0)
                //{
                //    if(Global.preQueue.obuQueue.Count < Global.raster.VehCount)
                //    {

                //    }
                //}
            }

            UpdateMeterMsgUI("光栅到来信号到来", Color.Orange, rtb);
        }

        public void HandleRasterComeMsg(RichTextBox rtb)
        {
            //光栅触发
            if (Global.raster.RasterStatus == Raster.RasterStat.Trigger)
            {
                //线圈4遮挡
                if (Global.coils.CoilStatus[3].StatNow == Coil.CurStatus.Sheltered)
                {
                    g_u8PassStatus = 1;
                }
                //if((Global.raster.VehStatWord & 1) ==0 && Global.preQueue.obuQueue.Count>0 && Global.raster.VehCount>0)
                //{
                //    if(Global.preQueue.obuQueue.Count < Global.raster.VehCount)
                //    {

                //    }
                //}
            }

            UpdateMeterMsgUI("车辆收尾帧到来", Color.OrangeRed, rtb);
        }

        #endregion


        #region 运行参数

        private void UpdateRunParamsMsg( string msg, Color color, RichTextBox rtb)
        {
            if (InitParams.UIEnabled)
            {
                RichTextBoxUtil.SetRTxtUI(rtb, msg, color);
            }
        }

        public void HandleShowRunParams(RichTextBox rtb)
        {
            string msg = string.Format("g_u8waitPrdCarNum = {0},g_u8PrdCarNum = {1},g_u8TrdCarNum = {2},g_u8PassStatus = {3}\r\n" +
                "preStep = {4} , trdStep={5} ,preAntanaOBUNo={6},trdAntanaOBUNo={7}",
                g_u8waitPrdCarNum, g_u8PrdCarNum, g_u8TrdCarNum, g_u8PassStatus,
                preStep, trdStep, preAntanaOBUNo, trdAntanaOBUNo);
            UpdateRunParamsMsg(msg, Color.Blue, rtb);
        }

        public void HandleReSetParamsCmd()
        {
            g_u8waitPrdCarNum = 0;

            g_u8PrdCarNum = 0;

            g_u8TrdCarNum = 0;

            g_u8PassStatus = 0;

            prdAlartStat = AlartStat.NonAlarmed;

            trdAlartStat = AlartStat.NonAlarmed;

            Global.preQueue.Clear();
            Global.preOBUQueue.Clear();
            Global.exchangeQueue.Clear();
        }

        #endregion
    }
}
