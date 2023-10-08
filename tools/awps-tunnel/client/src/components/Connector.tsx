import "./Connector.css";
import { ConnectionStatus, ConnectionStatusPair } from "../models";
import { CaretLeft24Filled as ArrowLeft, CaretRight24Filled as ArrowRight } from '@fluentui/react-icons';
import {
  makeStyles,
  shorthands,
  tokens,
  Divider,
  PresenceBadge,
} from "@fluentui/react-components";
const useStyles = makeStyles({
  arrowLeft: {
    color: tokens.colorPaletteRedBorder2,
    position: "absolute",
    top: "-2px",
    left: "-1px",
  },
  arrowRight: {
    color: tokens.colorPaletteRedBorder2,
    position: "absolute",
    top: "-2px",
    right: "-10px",
  },
  customLineStyle: {
    ...shorthands.borderWidth("2px"),
    "::before": {
      borderTopStyle: "dashed",
      borderTopWidth: "2px",
      ...shorthands.borderColor(tokens.colorPaletteRedBorder2),
      ...shorthands.borderLeft( "-10px solid #ccc"),
      ...shorthands.borderRight( "-10px solid #007bff"),

    },
    "::after": {
      borderTopStyle: "dashed",
      borderTopWidth: "2px",
      ...shorthands.borderColor(tokens.colorPaletteRedBorder2),
    },
  },
});

export function Connector({ status }: { status: ConnectionStatus }) {
  const styles = useStyles();
  return <div className="container position-relative">
    <ArrowLeft className={styles.arrowLeft}>
      </ArrowLeft>
      <Divider className={styles.customLineStyle} alignContent="center" ><PresenceBadge></PresenceBadge>
      
    <ArrowRight className={styles.arrowRight}>
      </ArrowRight>
  </Divider></div>;

  if (status === ConnectionStatus.Connecting || status === ConnectionStatus.None) {
    return <div className="dashed-line arrow-line connecting"></div>;
    
  }

  if (status === ConnectionStatus.Connected) {
    return <div className="arrow-line connected"></div>;
  }

  return <div className="dashed-line arrow-line"></div>;
}

export function TwoDirectionConnector({ statusPair }: { statusPair: ConnectionStatusPair }) {
  if (statusPair.statusOut === ConnectionStatus.None || statusPair.statusIn === ConnectionStatus.None) {
    return <div className="two-direction-arrow-line dashed-line"></div>;

  }
  if (statusPair.statusOut === ConnectionStatus.Connected && statusPair.statusIn === ConnectionStatus.Connected) {
    return <div className="two-direction-arrow-line connected"></div>;
  }

  if (statusPair.statusOut === ConnectionStatus.Disconnected) {
    return <div className="two-direction-arrow-line requesterror"></div>;
  }

  if (statusPair.statusOut === ConnectionStatus.Connected && statusPair.statusIn === ConnectionStatus.Disconnected) {
    return <div className="two-direction-arrow-line responseerror"></div>;
  }

  return <div className="two-direction-arrow-line dashed-line"></div>;
}
