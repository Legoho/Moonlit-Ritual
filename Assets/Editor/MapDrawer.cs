using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(Map))]
public class MapDrawer : PropertyDrawer
{
    private const int MinimumRows = 7;
    private const int MinimumColumns = 7;

    private const int VisibleRows = 7;
    private const int VisibleColumns = 7;

    private const float Spacing = 2f;
    private const float RowLabelWidth = 28f;
    private const float CellWidth = 100f;
    private const float CellHeight = 20f;
    private const float ScrollbarSize = 16f;

    private readonly Dictionary<string, Vector2> scrollPositions = new();

    public override void OnGUI(
        Rect position,
        SerializedProperty property,
        GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty rowsProperty =
            property.FindPropertyRelative("rows");

        SerializedProperty columnsProperty =
            property.FindPropertyRelative("columns");

        SerializedProperty tilesProperty =
            property.FindPropertyRelative("mapTiles");

        float lineHeight = EditorGUIUtility.singleLineHeight;

        Rect foldoutRect = new Rect(
            position.x,
            position.y,
            position.width,
            lineHeight
        );

        property.isExpanded = EditorGUI.Foldout(
            foldoutRect,
            property.isExpanded,
            label,
            true
        );

        if (!property.isExpanded)
        {
            EditorGUI.EndProperty();
            return;
        }

        if (rowsProperty == null ||
            columnsProperty == null ||
            tilesProperty == null)
        {
            Rect errorRect = new Rect(
                position.x,
                foldoutRect.yMax + Spacing,
                position.width,
                lineHeight * 2f
            );

            EditorGUI.HelpBox(
                errorRect,
                "MapDrawer could not find rows, columns, or mapTiles.",
                MessageType.Error
            );

            EditorGUI.EndProperty();
            return;
        }

        EnsureMinimumDimensions(
            rowsProperty,
            columnsProperty,
            tilesProperty
        );

        Rect contentRect = EditorGUI.IndentedRect(position);

        Rect dimensionsRect = new Rect(
            contentRect.x,
            foldoutRect.yMax + Spacing,
            contentRect.width,
            lineHeight
        );

        DrawDimensions(
            dimensionsRect,
            rowsProperty,
            columnsProperty,
            tilesProperty
        );

        int rows = Mathf.Max(
            MinimumRows,
            rowsProperty.intValue
        );

        int columns = Mathf.Max(
            MinimumColumns,
            columnsProperty.intValue
        );

        EnsureCorrectArraySize(
            tilesProperty,
            rows,
            columns
        );

        DrawScrollableTable(
            contentRect,
            dimensionsRect,
            property,
            tilesProperty,
            rows,
            columns
        );

        EditorGUI.EndProperty();
    }

    private void DrawScrollableTable(
        Rect contentRect,
        Rect dimensionsRect,
        SerializedProperty mapProperty,
        SerializedProperty tilesProperty,
        int rows,
        int columns)
    {
        float preferredViewportWidth =
            RowLabelWidth +
            VisibleColumns * (CellWidth + Spacing) +
            ScrollbarSize;

        float viewportWidth = Mathf.Min(
            contentRect.width,
            preferredViewportWidth
        );

        float viewportHeight =
            GetViewportHeight();

        Rect viewportRect = new Rect(
            contentRect.x,
            dimensionsRect.yMax + Spacing,
            viewportWidth,
            viewportHeight
        );

        float tableWidth =
            RowLabelWidth +
            columns * (CellWidth + Spacing);

        // Add one row for the column-number header.
        float tableHeight =
            (rows + 1) * (CellHeight + Spacing);

        Rect tableRect = new Rect(
            0f,
            0f,
            tableWidth,
            tableHeight
        );

        string scrollKey =
            mapProperty.serializedObject.targetObject.GetInstanceID() +
            ":" +
            mapProperty.propertyPath;

        if (!scrollPositions.TryGetValue(
                scrollKey,
                out Vector2 scrollPosition))
        {
            scrollPosition = Vector2.zero;
        }

        scrollPosition = GUI.BeginScrollView(
            viewportRect,
            scrollPosition,
            tableRect,
            true,
            true
        );

        DrawTableContents(
            tilesProperty,
            rows,
            columns
        );

        GUI.EndScrollView();

        scrollPositions[scrollKey] = scrollPosition;
    }

    private static void DrawTableContents(
        SerializedProperty tilesProperty,
        int rows,
        int columns)
    {
        GUIStyle headerStyle = new GUIStyle(
            EditorStyles.miniLabel
        )
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        Rect cornerRect = new Rect(
            0f,
            0f,
            RowLabelWidth,
            CellHeight
        );

        EditorGUI.LabelField(
            cornerRect,
            "#",
            headerStyle
        );

        DrawColumnNumbers(columns, headerStyle);

        for (int row = 0; row < rows; row++)
        {
            DrawMapRow(
                tilesProperty,
                row,
                columns,
                headerStyle
            );
        }
    }

    private static void DrawColumnNumbers(
        int columns,
        GUIStyle headerStyle)
    {
        for (int column = 0; column < columns; column++)
        {
            float columnX =
                RowLabelWidth +
                column * (CellWidth + Spacing);

            Rect columnHeaderRect = new Rect(
                columnX,
                0f,
                CellWidth,
                CellHeight
            );

            EditorGUI.LabelField(
                columnHeaderRect,
                column.ToString(),
                headerStyle
            );
        }
    }

    private static void DrawMapRow(
        SerializedProperty tilesProperty,
        int row,
        int columns,
        GUIStyle headerStyle)
    {
        float rowY =
            (row + 1) * (CellHeight + Spacing);

        Rect rowLabelRect = new Rect(
            0f,
            rowY,
            RowLabelWidth,
            CellHeight
        );

        EditorGUI.LabelField(
            rowLabelRect,
            row.ToString(),
            headerStyle
        );

        for (int column = 0; column < columns; column++)
        {
            DrawMapCell(
                tilesProperty,
                row,
                column,
                columns,
                rowY
            );
        }
    }

    private static void DrawMapCell(
        SerializedProperty tilesProperty,
        int row,
        int column,
        int columns,
        float rowY)
    {
        int index = row * columns + column;

        if (index < 0 || index >= tilesProperty.arraySize)
            return;

        float columnX =
            RowLabelWidth +
            column * (CellWidth + Spacing);

        Rect cellRect = new Rect(
            columnX,
            rowY,
            CellWidth,
            CellHeight
        );

        SerializedProperty tileProperty =
            tilesProperty.GetArrayElementAtIndex(index);

        EditorGUI.PropertyField(
            cellRect,
            tileProperty,
            GUIContent.none
        );
    }

    private static void DrawDimensions(
        Rect rect,
        SerializedProperty rowsProperty,
        SerializedProperty columnsProperty,
        SerializedProperty tilesProperty)
    {
        int oldRows = Mathf.Max(
            MinimumRows,
            rowsProperty.intValue
        );

        int oldColumns = Mathf.Max(
            MinimumColumns,
            columnsProperty.intValue
        );

        float fieldWidth =
            (rect.width - Spacing) / 2f;

        Rect rowsRect = new Rect(
            rect.x,
            rect.y,
            fieldWidth,
            rect.height
        );

        Rect columnsRect = new Rect(
            rect.x + fieldWidth + Spacing,
            rect.y,
            fieldWidth,
            rect.height
        );

        EditorGUI.BeginChangeCheck();

        int newRows = Mathf.Max(
            MinimumRows,
            EditorGUI.IntField(
                rowsRect,
                "Rows",
                oldRows
            )
        );

        int newColumns = Mathf.Max(
            MinimumColumns,
            EditorGUI.IntField(
                columnsRect,
                "Columns",
                oldColumns
            )
        );

        if (!EditorGUI.EndChangeCheck())
            return;

        ResizeArray(
            tilesProperty,
            oldRows,
            oldColumns,
            newRows,
            newColumns
        );

        rowsProperty.intValue = newRows;
        columnsProperty.intValue = newColumns;
    }

    private static void ResizeArray(
        SerializedProperty tilesProperty,
        int oldRows,
        int oldColumns,
        int newRows,
        int newColumns)
    {
        int[] oldValues =
            new int[tilesProperty.arraySize];

        for (int i = 0;
             i < tilesProperty.arraySize;
             i++)
        {
            oldValues[i] =
                tilesProperty
                    .GetArrayElementAtIndex(i)
                    .intValue;
        }

        tilesProperty.arraySize =
            newRows * newColumns;

        // Initialize every cell as Empty.
        for (int i = 0;
             i < tilesProperty.arraySize;
             i++)
        {
            tilesProperty
                .GetArrayElementAtIndex(i)
                .intValue = (int)MapCellType.Floor;
        }

        int copiedRows =
            Mathf.Min(oldRows, newRows);

        int copiedColumns =
            Mathf.Min(oldColumns, newColumns);

        for (int row = 0;
             row < copiedRows;
             row++)
        {
            for (int column = 0;
                 column < copiedColumns;
                 column++)
            {
                int oldIndex =
                    row * oldColumns + column;

                int newIndex =
                    row * newColumns + column;

                if (oldIndex >= oldValues.Length)
                    continue;

                tilesProperty
                    .GetArrayElementAtIndex(newIndex)
                    .intValue = oldValues[oldIndex];
            }
        }
    }

    private static void EnsureMinimumDimensions(
        SerializedProperty rowsProperty,
        SerializedProperty columnsProperty,
        SerializedProperty tilesProperty)
    {
        int oldRows = Mathf.Max(
            1,
            rowsProperty.intValue
        );

        int oldColumns = Mathf.Max(
            1,
            columnsProperty.intValue
        );

        int newRows = Mathf.Max(
            MinimumRows,
            oldRows
        );

        int newColumns = Mathf.Max(
            MinimumColumns,
            oldColumns
        );

        if (newRows == oldRows &&
            newColumns == oldColumns)
        {
            return;
        }

        ResizeArray(
            tilesProperty,
            oldRows,
            oldColumns,
            newRows,
            newColumns
        );

        rowsProperty.intValue = newRows;
        columnsProperty.intValue = newColumns;
    }

    private static void EnsureCorrectArraySize(
        SerializedProperty tilesProperty,
        int rows,
        int columns)
    {
        int requiredSize = rows * columns;

        if (tilesProperty.arraySize == requiredSize)
            return;

        int oldSize = tilesProperty.arraySize;

        tilesProperty.arraySize = requiredSize;

        // Unity may copy the final array element when expanding.
        // Explicitly make new cells Empty instead.
        for (int i = oldSize; i < requiredSize; i++)
        {
            tilesProperty
                .GetArrayElementAtIndex(i)
                .intValue = (int)MapCellType.Floor;
        }
    }

    private static float GetViewportHeight()
    {
        // One additional visible row is reserved for column numbers.
        return
            (VisibleRows + 1) *
            (CellHeight + Spacing) +
            ScrollbarSize;
    }

    public override float GetPropertyHeight(
        SerializedProperty property,
        GUIContent label)
    {
        float lineHeight =
            EditorGUIUtility.singleLineHeight;

        if (!property.isExpanded)
            return lineHeight;

        // Foldout + spacing + dimensions + spacing + viewport.
        return
            lineHeight +
            Spacing +
            lineHeight +
            Spacing +
            GetViewportHeight();
    }
}