namespace DAP.Infrastructure.DataAccess.Queries;

/// <summary>
/// 定义平台只读查询 SQL。
/// </summary>
public static class PlatformReadSql
{
    /// <summary>
    /// 采集点列表查询。
    /// </summary>
    public const string CollectionPoints =
        """
        SELECT
            p.id AS "Id",
            p.code AS "Code",
            p.name AS "Name",
            p.protocol AS "Protocol",
            p.endpoint AS "Endpoint",
            p.is_enabled AS "IsEnabled",
            p.communication_status AS "CommunicationStatus",
            p.source AS "Source",
            p.last_error AS "LastError",
            p.updated_at AS "UpdatedAt"
        FROM collection_points p
        ORDER BY p.code;
        """;

    /// <summary>
    /// 后台数据定义列表查询。
    /// </summary>
    public const string ManagedDataDefinitions =
        """
        SELECT
            d.id AS "Id",
            d.code AS "Code",
            d.name AS "Name",
            d.acquisition_type AS "AcquisitionType",
            d.connection_address AS "ConnectionAddress",
            d.identifier AS "Identifier",
            d.department AS "Department",
            d.process_code AS "ProcessCode",
            d.data_category AS "DataCategory",
            d.unit AS "Unit",
            d.description AS "Description",
            d.configuration_json::text AS "ConfigurationJson",
            d.business_tags_json::text AS "BusinessTagsJson",
            d.collection_interval_seconds AS "CollectionIntervalSeconds",
            d.is_enabled AS "IsEnabled",
            d.updated_at AS "UpdatedAt"
        FROM managed_data_definitions d
        ORDER BY d.acquisition_type, d.code;
        """;

    /// <summary>
    /// 后台数据定义详情查询。
    /// </summary>
    public const string ManagedDataDefinitionById =
        """
        SELECT
            d.id AS "Id",
            d.code AS "Code",
            d.name AS "Name",
            d.acquisition_type AS "AcquisitionType",
            d.connection_address AS "ConnectionAddress",
            d.identifier AS "Identifier",
            d.department AS "Department",
            d.process_code AS "ProcessCode",
            d.data_category AS "DataCategory",
            d.unit AS "Unit",
            d.description AS "Description",
            d.configuration_json::text AS "ConfigurationJson",
            d.business_tags_json::text AS "BusinessTagsJson",
            d.collection_interval_seconds AS "CollectionIntervalSeconds",
            d.is_enabled AS "IsEnabled",
            d.updated_at AS "UpdatedAt"
        FROM managed_data_definitions d
        WHERE d.id = @Id;
        """;

    /// <summary>
    /// 采集数据列表查询。
    /// </summary>
    public const string CollectionData =
        """
        SELECT
            r.id AS "Id",
            r.collection_point_id AS "CollectionPointId",
            p.code AS "CollectionPointCode",
            p.name AS "CollectionPointName",
            r.metric_name AS "MetricName",
            r.value AS "Value",
            r.unit AS "Unit",
            r.collected_at AS "CollectedAt"
        FROM collection_data_records r
        INNER JOIN collection_points p ON p.id = r.collection_point_id
        ORDER BY r.collected_at DESC
        LIMIT @Limit;
        """;

    /// <summary>
    /// 根据采集点编码获取采集点。
    /// </summary>
    public const string CollectionPointByCode =
        """
        SELECT
            p.id AS "Id",
            p.code AS "Code",
            p.name AS "Name",
            p.protocol AS "Protocol",
            p.endpoint AS "Endpoint",
            p.is_enabled AS "IsEnabled",
            p.communication_status AS "CommunicationStatus",
            p.source AS "Source",
            p.last_error AS "LastError",
            p.updated_at AS "UpdatedAt"
        FROM collection_points p
        WHERE p.code = @Code;
        """;

    /// <summary>
    /// 根据采集点编码获取历史采集数据。
    /// </summary>
    public const string CollectionDataByPointCode =
        """
        SELECT
            r.id AS "Id",
            r.collection_point_id AS "CollectionPointId",
            p.code AS "CollectionPointCode",
            p.name AS "CollectionPointName",
            r.metric_name AS "MetricName",
            r.value AS "Value",
            r.unit AS "Unit",
            r.collected_at AS "CollectedAt"
        FROM collection_data_records r
        INNER JOIN collection_points p ON p.id = r.collection_point_id
        WHERE p.code = @Code
        ORDER BY r.collected_at DESC
        LIMIT @Limit;
        """;

    /// <summary>
    /// 概览汇总查询。
    /// </summary>
    public const string DashboardSummary =
        """
        SELECT
            COUNT(*)::integer AS "TotalCollectionPoints",
            COUNT(*) FILTER (WHERE communication_status = '在线')::integer AS "OnlineCollectionPoints",
            COUNT(*) FILTER (WHERE communication_status <> '在线')::integer AS "OfflineCollectionPoints",
            COUNT(*) FILTER (WHERE source = 'Local')::integer AS "LocalSourcedCollectionPoints"
        FROM collection_points;
        """;
}
