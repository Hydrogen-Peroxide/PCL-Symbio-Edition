using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PaperConnect.Core.Room;

public class RoomCodeGenerator
{
    private static readonly char[] CHAR_SET = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();
    private static readonly int CHAR_COUNT = CHAR_SET.Length;

    public static readonly string ROOM_NAME = "paper-connect";
    public static readonly string ROOM_CODE_HEADER = @"P/";

    // 创建字符到索引的映射
    private static readonly Dictionary<char, int> CharToIndex = new();

    static RoomCodeGenerator()
    {
        for (int i = 0; i < CHAR_SET.Length; i++)
        {
            CharToIndex[CHAR_SET[i]] = i;
        }
    }

    /// <summary>
    /// 生成符合格式的联机房间码
    /// </summary>
    /// <returns>格式为 ?/NNNN-NNNN-SSSS-SSSS 的房间码</returns>
    public static string GenerateRoomCode()
    {
        var random = new Random();

        // 生成前8个字符(N部分)
        char[] nPart = new char[8];
        for (int i = 0; i < 8; i++)
        {
            nPart[i] = CHAR_SET[random.Next(0, CHAR_COUNT)];
        }

        // 生成后8个字符(S部分)
        char[] sPart = new char[8];
        for (int i = 0; i < 8; i++)
        {
            sPart[i] = CHAR_SET[random.Next(0, CHAR_COUNT)];
        }

        // 调整S部分使其转换后的数值能被7整除
        AdjustForDivisibilityBySeven(sPart);

        // 组装成房间码
        string nFormatted = $"{new string(nPart[..4])}-{new string(nPart[4..])}";
        string sFormatted = $"{new string(sPart[..4])}-{new string(sPart[4..])}";

        return $"{ROOM_CODE_HEADER}{nFormatted}-{sFormatted}";
    }

    /// <summary>
    /// 解析房间码并返回网络信息
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <returns>包含网络名称和密钥的对象</returns>
    public static NetworkInfo ParseRoomCode(string roomCode)
    {
        if (!IsValidFormat(roomCode))
        {
            throw new ArgumentException("Invalid room code format");
        }

        // 提取N部分和S部分
        string[] parts = roomCode.Substring(2).Split('-');
        string nPart = parts[0] + parts[1]; // NNNN-NNNN -> NNNNNNNN
        string sPart = parts[2] + parts[3]; // SSSS-SSSS -> SSSSSSSS

        string networkName = $"{ROOM_NAME}-{parts[0]}-{parts[1]}";
        string networkKey = $"{parts[2]}-{parts[3]}";

        return new NetworkInfo
        {
            NetworkName = networkName,
            NetworkKey = networkKey,
            NPart = nPart,
            SPart = sPart
        };
    }

    /// <summary>
    /// 验证房间码格式
    /// </summary>
    /// <param name="roomCode">房间码</param>
    /// <returns>是否有效</returns>
    public static bool IsValidFormat(string roomCode)
    {
        if (string.IsNullOrEmpty(roomCode) || !roomCode.StartsWith(ROOM_CODE_HEADER))
            return false;

        string content = roomCode.Substring(2);
        string[] parts = content.Split('-');

        if (parts.Length != 4)
            return false;

        foreach (string part in parts)
        {
            if (part.Length != 4)
                return false;

            foreach (char c in part)
            {
                if (!CharToIndex.ContainsKey(c))
                    return false;
            }
        }

        // 验证S部分是否能被7整除
        string sPart = parts[2] + parts[3];
        return IsDivisibleBySeven(sPart);
    }

    /// <summary>
    /// 将字符序列转换为小端序整数并验证是否能被7整除
    /// </summary>
    /// <param name="chars">字符序列</param>
    /// <returns>是否能被7整除</returns>
    private static bool IsDivisibleBySeven(string chars)
    {
        long value = ConvertToLong(chars);
        return value % 7 == 0;
    }

    /// <summary>
    /// 将字符序列转换为小端序长整型值
    /// </summary>
    /// <param name="chars">字符序列</param>
    /// <returns>对应的长整型值</returns>
    private static long ConvertToLong(string chars)
    {
        long result = 0;
        long multiplier = 1;

        for (int i = 0; i < chars.Length; i++)
        {
            int index = CharToIndex[chars[i]];
            result += index * multiplier;
            multiplier *= CHAR_COUNT; // 34进制
        }

        return result;
    }

    /// <summary>
    /// 调整字符序列使转换后的数值能被7整除
    /// </summary>
    /// <param name="chars">待调整的字符数组</param>
    private static void AdjustForDivisibilityBySeven(char[] chars)
    {
        long currentValue = ConvertToLong(new string(chars));
        long remainder = currentValue % 7;

        if (remainder == 0) return; // 已经能被7整除

        // 计算需要减少多少才能被7整除
        long adjustment = remainder;

        // 从低位开始调整字符
        for (int i = 0; i < chars.Length && adjustment > 0; i++)
        {
            int currentIndex = CharToIndex[chars[i]];
            long positionValue = (long)Math.Pow(CHAR_COUNT, i); // 34^i

            // 计算当前位置最多能减少多少
            long maxReductionAtThisPosition = Math.Min(adjustment, (long)currentIndex * positionValue);
            long reductionSteps = maxReductionAtThisPosition / positionValue;

            if (reductionSteps > 0)
            {
                // 更新字符
                int newIndex = currentIndex - (int)reductionSteps;
                if (newIndex < 0) newIndex = 0; // 确保索引不小于0

                chars[i] = CHAR_SET[newIndex];
                adjustment -= reductionSteps * positionValue;
            }
        }

        // 如果还有余数，继续微调
        currentValue = ConvertToLong(new string(chars));
        remainder = currentValue % 7;

        if (remainder != 0)
        {
            // 微调最后一个字符来达到整除效果
            int lastIdx = chars.Length - 1;
            int currentIndex = CharToIndex[chars[lastIdx]];
            long positionValue = (long)Math.Pow(CHAR_COUNT, lastIdx);

            // 计算需要增加还是减少
            long neededAdjustment = (7 - remainder) % 7;
            long steps = neededAdjustment / positionValue;

            if (steps > 0)
            {
                int newIndex = Math.Min(currentIndex + (int)steps, CHAR_COUNT - 1);
                chars[lastIdx] = CHAR_SET[newIndex];
            }
            else if (steps < 0)
            {
                int newIndex = Math.Max(currentIndex + (int)steps, 0);
                chars[lastIdx] = CHAR_SET[newIndex];
            }
        }

        // 最终验证
        currentValue = ConvertToLong(new string(chars));
        if (currentValue % 7 != 0)
        {
            // 如果仍然不能整除，则逐个调整直到满足条件
            while (currentValue % 7 != 0)
            {
                // 增加最后一位字符的值
                int lastIdx = chars.Length - 1;
                int currentIndex = CharToIndex[chars[lastIdx]];

                if (currentIndex < CHAR_COUNT - 1)
                {
                    chars[lastIdx] = CHAR_SET[currentIndex + 1];
                }
                else
                {
                    // 如果已经是最大值，则调整前一位
                    for (int i = lastIdx - 1; i >= 0; i--)
                    {
                        int idx = CharToIndex[chars[i]];
                        if (idx < CHAR_COUNT - 1)
                        {
                            chars[i] = CHAR_SET[idx + 1];
                            // 将后面位置设为最小值
                            for (int j = i + 1; j < chars.Length; j++)
                            {
                                chars[j] = CHAR_SET[0];
                            }

                            break;
                        }
                    }
                }

                currentValue = ConvertToLong(new string(chars));
            }
        }
    }
}

/// <summary>
/// 网络信息类
/// </summary>
public class NetworkInfo
{
    public string NetworkName { get; set; }
    public string NetworkKey { get; set; }
    public string NPart { get; set; }
    public string SPart { get; set; }
}
