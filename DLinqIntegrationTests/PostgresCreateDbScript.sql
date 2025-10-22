--
-- PostgreSQL database dump
--

-- Dumped from database version 18.0
-- Dumped by pg_dump version 18.0

-- Started on 2025-10-22 17:09:05

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;


--
-- TOC entry 4976 (class 0 OID 0)
-- Dependencies: 4
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: pg_database_owner
--


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 223 (class 1259 OID 16421)
-- Name: Addresses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public."Addresses" (
    "Id" integer NOT NULL,
    "Addr1" character varying(64),
    "City" character varying(64),
    "State" character varying(64),
    "Zip" character varying(10)
);


ALTER TABLE public."Addresses" OWNER TO postgres;

--
-- TOC entry 222 (class 1259 OID 16420)
-- Name: Addresses_Id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public."Addresses" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public."Addresses_Id_seq"
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 221 (class 1259 OID 16410)
-- Name: person; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.person (
    id integer NOT NULL,
    firstname character varying(128),
    lastname character varying(128),
    age integer,
    createdateutc timestamp with time zone
);


ALTER TABLE public.person OWNER TO postgres;

--
-- TOC entry 220 (class 1259 OID 16409)
-- Name: person_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.person ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.person_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 219 (class 1259 OID 16403)
-- Name: person_uuid; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.person_uuid (
    id uuid NOT NULL,
    firstname character varying(128),
    lastname character varying(128),
    age integer,
    createdateutc timestamp without time zone
);


ALTER TABLE public.person_uuid OWNER TO postgres;

--
-- TOC entry 4823 (class 2606 OID 16426)
-- Name: Addresses _addresses__pk; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public."Addresses"
    ADD CONSTRAINT _addresses__pk PRIMARY KEY ("Id");


--
-- TOC entry 4821 (class 2606 OID 16415)
-- Name: person person_pk; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.person
    ADD CONSTRAINT person_pk PRIMARY KEY (id);


--
-- TOC entry 4819 (class 2606 OID 16408)
-- Name: person_uuid personu_uid_pk; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.person_uuid
    ADD CONSTRAINT personu_uid_pk PRIMARY KEY (id);


-- Completed on 2025-10-22 17:09:06

--
-- PostgreSQL database dump complete
--
