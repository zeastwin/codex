using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.VisualBasic;
using EW_Assistant.Domain.Inventory;
using EW_Assistant.ViewModels;

namespace EW_Assistant.ViewModels.Inventory
{
    /// <summary>
    /// 库存管理视图模型，提供基本的备件 CRUD 与库存操作。
    /// </summary>
    public class InventoryViewModel : ViewModelBase
    {
        private readonly IInventoryRepository _repository;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _deleteCommand;
        private readonly RelayCommand _stockInCommand;
        private readonly RelayCommand _stockOutCommand;
        private readonly RelayCommand _adjustCommand;

        private ObservableCollection<SparePart> _parts = new ObservableCollection<SparePart>();
        private SparePart _selectedPart;
        private bool _isRefreshing;

        public InventoryViewModel(IInventoryRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException("repository");
            }

            _repository = repository;

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
            AddPartCommand = new RelayCommand(async _ => await AddPartAsync());
            _editCommand = new RelayCommand(async _ => await EditPartAsync(), _ => SelectedPart != null);
            _deleteCommand = new RelayCommand(async _ => await DeletePartAsync(), _ => SelectedPart != null);
            _stockInCommand = new RelayCommand(async _ => await StockInAsync(), _ => SelectedPart != null);
            _stockOutCommand = new RelayCommand(async _ => await StockOutAsync(), _ => SelectedPart != null);
            _adjustCommand = new RelayCommand(async _ => await AdjustStockAsync(), _ => SelectedPart != null);

            EditPartCommand = _editCommand;
            DeletePartCommand = _deleteCommand;
            StockInCommand = _stockInCommand;
            StockOutCommand = _stockOutCommand;
            AdjustStockCommand = _adjustCommand;

            // 初次加载
            var _ = RefreshAsync();
        }

        public ObservableCollection<SparePart> Parts
        {
            get { return _parts; }
            private set { SetProperty(ref _parts, value); }
        }

        public SparePart SelectedPart
        {
            get { return _selectedPart; }
            set
            {
                if (SetProperty(ref _selectedPart, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public ICommand RefreshCommand { get; private set; }

        public ICommand AddPartCommand { get; private set; }

        public ICommand EditPartCommand { get; private set; }

        public ICommand DeletePartCommand { get; private set; }

        public ICommand StockInCommand { get; private set; }

        public ICommand StockOutCommand { get; private set; }

        public ICommand AdjustStockCommand { get; private set; }

        /// <summary>
        /// 刷新并应用搜索过滤。
        /// </summary>
        public async Task RefreshAsync()
        {
            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing = true;
            try
            {
                var list = await _repository.GetAllPartsAsync();
                UpdateParts(list);
            }
            catch (Exception ex)
            {
                MessageBox.Show("刷新库存失败：" + ex.Message, "库存管理", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void UpdateParts(IEnumerable<SparePart> items)
        {
            Parts.Clear();
            if (items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                Parts.Add(item);
            }
        }

        private async Task AddPartAsync()
        {
            var part = PromptPartInfo(new SparePart());
            if (part == null)
            {
                return;
            }

            try
            {
                await _repository.AddPartAsync(part);
                await RefreshAsync();
                MessageBox.Show("新增备件成功。", "库存管理", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("新增失败：" + ex.Message, "库存管理", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task EditPartAsync()
        {
            if (SelectedPart == null)
            {
                return;
            }
            var part = new SparePart
            {
                Id = SelectedPart.Id,
                PartNo = SelectedPart.PartNo,
                Name = SelectedPart.Name,
                Spec = SelectedPart.Spec,
                Unit = SelectedPart.Unit,
                Location = SelectedPart.Location,
                SafeStock = SelectedPart.SafeStock,
                MaxStock = SelectedPart.MaxStock,
                CurrentStock = SelectedPart.CurrentStock,
                IsActive = SelectedPart.IsActive,
                CreatedAt = SelectedPart.CreatedAt,
                UpdatedAt = SelectedPart.UpdatedAt
            };

            try
            {
                await _repository.UpdatePartAsync(part);
                await RefreshAsync();
                MessageBox.Show("更新成功。", "库存管理", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("更新失败：" + ex.Message, "库存管理", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeletePartAsync()
        {
            if (SelectedPart == null)
            {
                return;
            }

            var result = MessageBox.Show("确定删除该备件吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _repository.DeletePartAsync(SelectedPart.Id);
                await RefreshAsync();
                MessageBox.Show("删除成功。", "库存管理", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("删除失败：" + ex.Message, "库存管理", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StockInAsync()
        {
            if (SelectedPart == null)
            {
                return;
            }

            var qty = PromptInt("请输入入库数量", 1);
            if (qty <= 0)
            {
                MessageBox.Show("数量必须大于 0。", "入库", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reason = PromptText("入库原因", "补货");
            var refNo = PromptText("关联单号", string.Empty);
            var operatorName = PromptText("操作人", Environment.UserName);

            try
            {
                await _repository.StockInAsync(SelectedPart.Id, qty, reason, refNo, operatorName);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("入库失败：" + ex.Message, "入库", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StockOutAsync()
        {
            if (SelectedPart == null)
            {
                return;
            }

            var qty = PromptInt("请输入出库数量", 1);
            if (qty <= 0)
            {
                MessageBox.Show("数量必须大于 0。", "出库", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var reason = PromptText("出库原因", "领用");
            var refNo = PromptText("关联单号", string.Empty);
            var operatorName = PromptText("操作人", Environment.UserName);

            try
            {
                await _repository.StockOutAsync(SelectedPart.Id, qty, reason, refNo, operatorName);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("出库失败：" + ex.Message, "出库", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task AdjustStockAsync()
        {
            if (SelectedPart == null)
            {
                return;
            }

            var newQty = PromptInt("请输入调整后的库存数量", SelectedPart.CurrentStock);
            var reason = PromptText("调整原因", "盘点调整");
            var operatorName = PromptText("操作人", Environment.UserName);

            try
            {
                await _repository.AdjustStockAsync(SelectedPart.Id, newQty, reason, operatorName);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("调整库存失败：" + ex.Message, "库存调整", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private SparePart PromptPartInfo(SparePart source)
        {
            var partNo = PromptText("请输入料号", source.PartNo);
            if (string.IsNullOrWhiteSpace(partNo))
            {
                return null;
            }

            var name = PromptText("请输入名称", source.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var spec = PromptText("规格/型号", source.Spec);
            var unit = PromptText("计量单位", source.Unit);
            var location = PromptText("库位/存放位置", source.Location);
            var safeStock = PromptInt("安全库存", source.SafeStock);
            var maxStock = PromptInt("库存上限", source.MaxStock);
            var currentStock = PromptInt("当前库存", source.CurrentStock);

            var activeText = PromptText("是否启用（是/否，默认是）", source.IsActive ? "是" : "否");
            var isActive = ParseBool(activeText, source.IsActive);

            var part = new SparePart
            {
                Id = source.Id,
                PartNo = partNo.Trim(),
                Name = name.Trim(),
                Spec = spec,
                Unit = unit,
                Location = location,
                SafeStock = safeStock,
                MaxStock = maxStock,
                CurrentStock = currentStock,
                IsActive = isActive,
                CreatedAt = source.CreatedAt,
                UpdatedAt = source.UpdatedAt
            };

            return part;
        }

        private string PromptText(string prompt, string defaultValue)
        {
            return Interaction.InputBox(prompt, "库存管理", defaultValue ?? string.Empty);
        }

        private int PromptInt(string prompt, int defaultValue)
        {
            var input = Interaction.InputBox(prompt, "库存管理", defaultValue.ToString());
            int result;
            if (!int.TryParse(input, out result))
            {
                result = defaultValue;
            }
            return result;
        }

        private bool ParseBool(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            var text = value.Trim();
            if (string.Equals(text, "是", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "否", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }

        private void UpdateCommandStates()
        {
            _editCommand.RaiseCanExecuteChanged();
            _deleteCommand.RaiseCanExecuteChanged();
            _stockInCommand.RaiseCanExecuteChanged();
            _stockOutCommand.RaiseCanExecuteChanged();
            _adjustCommand.RaiseCanExecuteChanged();
        }
    }
}
