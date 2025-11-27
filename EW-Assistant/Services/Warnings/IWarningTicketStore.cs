using System.Collections.Generic;

namespace EW_Assistant.Warnings
{
    /// <summary>
    /// 预警工单存储接口。
    /// </summary>
    public interface IWarningTicketStore
    {
        IList<WarningTicketRecord> LoadAll();
        void SaveAll(IList<WarningTicketRecord> tickets);
    }
}
