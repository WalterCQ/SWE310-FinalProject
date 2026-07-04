namespace TaskFlow.Api.Data.DemoData;

public static class DemoDataIds
{
    public const string WorkspaceName = "TaskFlow Demo Workspace";

    public static readonly Guid WorkspaceId = Guid.Parse("0a8f6b9d-5f6c-4f9f-9a6d-6d9d59f2d101");

    public static readonly Guid GeneralChannelId = Guid.Parse("67ad312f-7817-47c5-a2f5-c2eb2fe44df1");
    public static readonly Guid FrontendChannelId = Guid.Parse("bb1352b4-d637-4fa7-aad5-f2a63325fbd6");
    public static readonly Guid ApiChannelId = Guid.Parse("dd3cd57e-ec6d-4fa6-b30e-c8de32d5a755");
    public static readonly Guid ReviewChannelId = Guid.Parse("63bb9c2b-cf8c-4124-914e-6dd6b1a4e05a");

    public static readonly Guid FrontendProjectId = Guid.Parse("d24e37d3-f4e2-4f58-88d8-b3ac5ea7091a");
    public static readonly Guid BackendProjectId = Guid.Parse("e5d33af5-2d23-47a3-85e4-94e3207f6012");
    public static readonly Guid AiProjectId = Guid.Parse("c914b257-7f91-470d-a6ee-0ace2463fb47");
    public static readonly Guid PresentationProjectId = Guid.Parse("f08ee42e-faf5-4b2d-8d2d-cb25d14e2962");

    public static readonly Guid[] ChannelIds =
    [
        GeneralChannelId,
        FrontendChannelId,
        ApiChannelId,
        ReviewChannelId
    ];

    public static readonly Guid[] ProjectIds =
    [
        FrontendProjectId,
        BackendProjectId,
        AiProjectId,
        PresentationProjectId
    ];
}
