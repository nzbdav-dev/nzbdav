import { memo } from "react";
import type { RequiredTopNavProps } from "../page-layout/page-layout";
import { useNavigate } from "react-router";
import styles from "./top-navigation.module.css";
import { HamburgerMenu } from "../hamburger-menu/hamburger-menu";
import { withUrlBase } from "~/utils/url-base";

export type TopNavigationProps = RequiredTopNavProps;

export const TopNavigation = memo(function TopNavigation(props: TopNavigationProps) {
  const { isHamburgerMenuOpen, onHamburgerMenuClick } = props;
  const navigate = useNavigate();

  return (
    <div className={styles["container"]}>
      <HamburgerMenu isOpen={isHamburgerMenuOpen} onClick={onHamburgerMenuClick} />
      <div className={styles["title-container"]} onClick={() => navigate("/")}>
        <img className={styles["logo"]} src={withUrlBase("/logo.svg")}></img>
        <div className={styles["title"]}>Nzb DAV</div>
      </div>
    </div>
  );
});