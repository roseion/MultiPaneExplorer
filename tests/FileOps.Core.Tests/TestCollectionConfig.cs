using Xunit;

// 多个测试类会操作真实回收站（删除/还原/清理），并行执行会互相干扰，改为顺序执行
[assembly: CollectionBehavior(DisableTestParallelization = true)]
