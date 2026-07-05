import { useI18n } from "../i18n.jsx";

const GRID_SIZE = 5;
const MIRROR_COLUMNS = 3;
const avatarColors = [
  "#07998d",
  "#053f3b",
  "#ef6845",
  "#61875d",
  "#dc4b3f",
  "#a06f08",
  "#0b0f0e",
];

function hashString(value) {
  let hash = 0x811c9dc5;
  const input = String(value || "");

  for (let index = 0; index < input.length; index += 1) {
    hash ^= input.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }

  return hash >>> 0;
}

function nextHash(value) {
  value ^= value << 13;
  value ^= value >>> 17;
  value ^= value << 5;
  return value >>> 0;
}

function getAvatarSeed(seed, name) {
  return [seed, name].filter(Boolean).join(":") || "taskflow-user";
}

function buildCells(seed) {
  const cells = [];
  let hash = hashString(seed);

  for (let row = 0; row < GRID_SIZE; row += 1) {
    for (let column = 0; column < MIRROR_COLUMNS; column += 1) {
      hash = nextHash(hash + row + column + 1);
      if ((hash & 1) === 0) continue;

      const mirroredColumn = GRID_SIZE - column - 1;
      cells.push({ row, column });
      if (mirroredColumn !== column) cells.push({ row, column: mirroredColumn });
    }
  }

  return cells;
}

function buildColor(seed) {
  const hash = hashString(seed);
  return avatarColors[hash % avatarColors.length];
}

export default function Avatar({ seed, name, className = "", ariaHidden = false }) {
  const { t } = useI18n();
  const avatarSeed = getAvatarSeed(seed, name);
  const label = name ? t("avatar.named", { name }) : t("avatar.generated");
  const cells = buildCells(avatarSeed);
  const style = {
    "--avatar-color": buildColor(avatarSeed),
  };

  return (
    <span
      className={`identicon-avatar ${className}`.trim()}
      role={ariaHidden ? undefined : "img"}
      aria-label={ariaHidden ? undefined : label}
      aria-hidden={ariaHidden || undefined}
      style={style}
    >
      <svg viewBox={`0 0 ${GRID_SIZE} ${GRID_SIZE}`} focusable="false" aria-hidden="true">
        <rect width={GRID_SIZE} height={GRID_SIZE} fill="var(--avatar-empty)" />
        {cells.map((cell) => (
          <rect
            key={`${cell.row}-${cell.column}`}
            x={cell.column}
            y={cell.row}
            width="1"
            height="1"
            fill="var(--avatar-color)"
            shapeRendering="crispEdges"
          />
        ))}
      </svg>
    </span>
  );
}
